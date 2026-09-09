using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Models.Ddf;
using DmxControlUtilities.Lib.Services;
using DmxControlUtilities.Lib.Services.Hal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Xml.Linq;

namespace DmxControlUtilities.Tests
{
    [TestClass]
    public class ProgrammerServiceTests
    {
        private DmxFtdiService mDmx = null!;
        private DeviceService mDeviceService = null!;
        private HalService mHal = null!;
        private DeviceDescription mDescription = null!;
        private Device mDevice = null!;
        private ProgrammerService mProgrammer = null!;
        private CuelistService mCuelistService = null!;

        [TestInitialize]
        public void Initialize()
        {
            mDescription = DdfParser.Parse(XDocument.Parse(@"
                <device><functions>
                    <dimmer dmxchannel='0'/>
                    <rgb>
                        <red dmxchannel='1'/>
                        <green dmxchannel='2'/>
                        <blue dmxchannel='3'/>
                    </rgb>
                    <position>
                        <pan dmxchannel='4' finedmxchannel='5'/>
                        <tilt dmxchannel='6' finedmxchannel='7'/>
                    </position>
                    <rawstep name='Program' dmxchannel='8'>
                        <step type='value' val='0' mindmx='0' maxdmx='127' caption='Off'/>
                        <step type='value' val='1' mindmx='128' maxdmx='255' caption='On'/>
                    </rawstep>
                </functions></device>"));
            mDescription.Id = "test";
            mDmx = new DmxFtdiService();
            mDeviceService = new DeviceService(mDmx, new DeviceDescriptionService(mDescription));
            mHal = new HalService(new DeviceDescriptionService(mDescription), mDeviceService);
            mDevice = mDeviceService.CreateDevice(mDescription, 1);
            mDeviceService.AddDevice(mDevice);

            mProgrammer = new ProgrammerService(mDeviceService, mHal);
            mCuelistService = new CuelistService(mDeviceService, mHal);
        }

        [TestCleanup]
        public void Cleanup()
        {
            mCuelistService.StopAll();
            mProgrammer.Clear();
            mDmx.Dispose();
        }

        [TestMethod]
        public void SetValue_OverridesOutputImmediately_WithoutMutatingBaseDeviceValues()
        {
            mDevice.SetValue("dimmer", 50);
            mDeviceService.ApplyDevice(mDevice);
            Assert.AreEqual((byte)50, mDmx.GetChannel(1));

            mProgrammer.SetValue(mDevice.Id, Dimmer(200));

            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            Assert.AreEqual((byte)50, mDevice.GetValue("dimmer"));
            Assert.IsTrue(mProgrammer.HasValues);
        }

        [TestMethod]
        public void Programmer_HasHigherPriorityThanCuelistPlayback()
        {
            mDevice.SetValue("dimmer", 10);
            mDeviceService.ApplyDevice(mDevice);

            var cue = new Cue
            {
                Name = "PlaybackCue",
                DeviceValues = new()
                {
                    [mDevice.Id] = new() { ["dimmer"] = Dimmer(100) }
                }
            };
            var cuelist = new Cuelist { Name = "List", Cues = new() { cue } };
            mCuelistService.AddCuelist(cuelist);
            mCuelistService.Go(cuelist.Id);

            // Cuelist playback overrides base
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));

            // Programmer overrides cuelist playback
            mProgrammer.SetValue(mDevice.Id, Dimmer(222));
            Assert.AreEqual((byte)222, mDmx.GetChannel(1));

            // Clearing programmer releases output back to cuelist playback
            mProgrammer.Clear();
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));

            // Stopping cuelist releases back to base device control
            mCuelistService.Stop(cuelist.Id);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void StagingDevice_EditsFeaturesAndCommitsSingleValuesPerFeature()
        {
            var staging = mProgrammer.GetStagingDevice(mDevice);
            var features = mProgrammer.GetStagingFeatures(staging);

            var dimmerFeature = features.FirstOrDefault(f => f.Type == FeatureType.Dimmer);
            var colorFeature = features.FirstOrDefault(f => f.Type == FeatureType.Color);
            Assert.IsNotNull(dimmerFeature);
            Assert.IsNotNull(colorFeature);

            // Setting features on staging does not immediately write to the DMX universe
            dimmerFeature.SetValue(0.8);
            ((HalService.ColorFeature)colorFeature).SetColor(255, 128, 0);
            Assert.AreEqual((byte)0, mDmx.GetChannel(1));
            Assert.AreEqual((byte)0, mDmx.GetChannel(2));

            // Committing pushes one value per feature to the programmer output
            mProgrammer.CommitStagingDevice(staging);
            Assert.AreEqual((byte)204, mDmx.GetChannel(1));
            Assert.AreEqual((byte)255, mDmx.GetChannel(2));
            Assert.AreEqual((byte)128, mDmx.GetChannel(3));

            // Base device controls are untouched
            Assert.AreEqual((byte)0, mDevice.GetValue("dimmer"));

            // Staged as one entry per feature (not per channel)
            var snapshot = mProgrammer.GetValuesSnapshot();
            Assert.AreEqual(2, snapshot[mDevice.Id].Count);
            Assert.IsInstanceOfType(snapshot[mDevice.Id]["dimmer"], typeof(ScalarCueValue));
            var color = (ColorCueValue)snapshot[mDevice.Id]["rgb"];
            Assert.AreEqual((255, 128, 0), (color.R, color.G, color.B));
        }

        [TestMethod]
        public void CommitStaging_StagesPositionAsSingleFeatureValue()
        {
            var staging = mProgrammer.GetStagingDevice(mDevice);
            var position = features(staging).First(f => f.Type == FeatureType.Position);
            ((HalService.PositionFeature)position).SetPosition(1.0, 0.0);

            mProgrammer.CommitStagingDevice(staging);

            var snapshot = mProgrammer.GetValuesSnapshot();
            Assert.AreEqual(1, snapshot[mDevice.Id].Count);
            var pos = (PositionCueValue)snapshot[mDevice.Id]["position"];
            Assert.AreEqual(1.0, pos.Pan, 0.01);
            Assert.AreEqual(0.0, pos.Tilt, 0.01);

            Assert.AreEqual((byte)255, mDmx.GetChannel(5)); // pan coarse
            Assert.AreEqual((byte)0, mDmx.GetChannel(7));  // tilt coarse
        }

        [TestMethod]
        public void StagingDevice_ReflectsExistingProgrammerValues()
        {
            mProgrammer.SetValue(mDevice.Id, Dimmer(120));

            var staging = mProgrammer.GetStagingDevice(mDevice);
            var dimmer = features(staging).First(f => f.Type == FeatureType.Dimmer);

            Assert.AreEqual(120 / 255.0, dimmer.GetValue(), 0.01);
        }

        [TestMethod]
        public void Clear_And_UndoClear_CycleWorksCorrectly()
        {
            mProgrammer.SetValue(mDevice.Id, Dimmer(123));
            Assert.AreEqual((byte)123, mDmx.GetChannel(1));
            Assert.IsTrue(mProgrammer.HasValues);
            Assert.IsFalse(mProgrammer.CanUndoClear);

            mProgrammer.Clear();
            Assert.AreEqual((byte)0, mDmx.GetChannel(1));
            Assert.IsFalse(mProgrammer.HasValues);
            Assert.IsTrue(mProgrammer.CanUndoClear);

            mProgrammer.UndoClear();
            Assert.AreEqual((byte)123, mDmx.GetChannel(1));
            Assert.IsTrue(mProgrammer.HasValues);
            Assert.IsFalse(mProgrammer.CanUndoClear);
        }

        [TestMethod]
        public void DeleteFeature_RemovesAllChannelsOfThatFeature()
        {
            mProgrammer.SetValue(mDevice.Id, Dimmer(100));
            mProgrammer.SetValue(mDevice.Id, new ColorCueValue { Feature = "rgb", Name = "Color", R = 200, G = 0, B = 0 });

            Assert.AreEqual((byte)100, mDmx.GetChannel(1));
            Assert.AreEqual((byte)200, mDmx.GetChannel(2));

            // Deleting the color feature releases all rgb channels at once
            mProgrammer.DeleteFeature(mDevice.Id, "rgb");
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));
            Assert.AreEqual((byte)0, mDmx.GetChannel(2));

            mProgrammer.DeleteDevice(mDevice.Id);
            Assert.AreEqual((byte)0, mDmx.GetChannel(1));
            Assert.IsFalse(mProgrammer.HasValues);
        }

        [TestMethod]
        public void LoadCue_OverwritesProgrammer_AndSetsFeatureOutput()
        {
            mProgrammer.SetValue(mDevice.Id, Dimmer(50));

            var cue = new Cue
            {
                Name = "LoadedCue",
                DeviceValues = new()
                {
                    [mDevice.Id] = new()
                    {
                        ["dimmer"] = Dimmer(180),
                        ["rgb"] = new ColorCueValue { Feature = "rgb", Name = "Color", R = 250, G = 0, B = 0 }
                    }
                }
            };

            mProgrammer.LoadCue(cue);

            var snapshot = mProgrammer.GetValuesSnapshot();
            Assert.AreEqual(180 / 255.0, ((ScalarCueValue)snapshot[mDevice.Id]["dimmer"]).Value, 0.01);
            Assert.AreEqual((byte)250, ((ColorCueValue)snapshot[mDevice.Id]["rgb"]).R);
            Assert.AreEqual((byte)180, mDmx.GetChannel(1));
            Assert.AreEqual((byte)250, mDmx.GetChannel(2));
        }

        [TestMethod]
        public void StoreProgrammerIntoCue_NewAndOverwrite_Workflow()
        {
            mProgrammer.SetValue(mDevice.Id, Dimmer(155));

            var cuelist = new Cuelist { Name = "Main List" };
            mCuelistService.AddCuelist(cuelist);

            // Store new cue
            mCuelistService.StoreProgrammerIntoCue(cuelist.Id, null, "First Cue", mProgrammer.GetValuesSnapshot());

            var fetchedList = mCuelistService.GetCuelist(cuelist.Id)!;
            Assert.AreEqual(1, fetchedList.Cues.Count);
            Assert.AreEqual("First Cue", fetchedList.Cues[0].Name);
            Assert.AreEqual(155 / 255.0, ((ScalarCueValue)fetchedList.Cues[0].DeviceValues[mDevice.Id]["dimmer"]).Value, 0.01);

            // Storing does not automatically clear the programmer (successive distribution)
            Assert.IsTrue(mProgrammer.HasValues);

            // Overwrite existing cue
            mProgrammer.SetValue(mDevice.Id, Dimmer(210));
            mCuelistService.StoreProgrammerIntoCue(cuelist.Id, fetchedList.Cues[0].Id, "First Cue Updated", mProgrammer.GetValuesSnapshot());

            fetchedList = mCuelistService.GetCuelist(cuelist.Id)!;
            Assert.AreEqual(1, fetchedList.Cues.Count);
            Assert.AreEqual("First Cue Updated", fetchedList.Cues[0].Name);
            Assert.AreEqual(210 / 255.0, ((ScalarCueValue)fetchedList.Cues[0].DeviceValues[mDevice.Id]["dimmer"]).Value, 0.01);
        }

        [TestMethod]
        public void StoreProgrammerIntoCue_WhilePlaying_ThrowsException()
        {
            var cue = new Cue { Name = "Q", DeviceValues = new() { [mDevice.Id] = new() { ["dimmer"] = Dimmer(10) } } };
            var list = new Cuelist { Name = "List", Cues = new() { cue } };
            mCuelistService.AddCuelist(list);
            mCuelistService.Go(list.Id);

            mProgrammer.SetValue(mDevice.Id, Dimmer(99));

            Assert.ThrowsException<InvalidOperationException>(() =>
                mCuelistService.StoreProgrammerIntoCue(list.Id, null, "New Cue", mProgrammer.GetValuesSnapshot()));
        }

        [TestMethod]
        public void GetEntries_GroupsCompositeFeaturesIntoSingleRows()
        {
            mProgrammer.SetValue(mDevice.Id, Dimmer(77));
            mProgrammer.SetValue(mDevice.Id, new ColorCueValue { Feature = "rgb", Name = "Color", R = 255, G = 128, B = 0 });
            mProgrammer.SetValue(mDevice.Id, new PositionCueValue { Feature = "position", Name = "Position", Pan = 0.5, Tilt = 0.25 });

            var entries = mProgrammer.GetEntries();

            // One row per feature: color is a single entry even though it drives 3 channels,
            // position is a single entry even though it drives 4 channels.
            Assert.AreEqual(3, entries.Count);

            var dimmer = entries.Single(e => e.FeatureName == "Dimmer");
            Assert.AreEqual(mDevice.Name, dimmer.DeviceName);
            Assert.AreEqual("30%", dimmer.DisplayValue);

            var color = entries.Single(e => e.FeatureName == "Color");
            Assert.AreEqual(CueFeatureValueKind.Color, color.Kind);
            Assert.AreEqual("#ff8000", color.DisplayValue);

            var position = entries.Single(e => e.FeatureName == "Position");
            Assert.AreEqual("50% / 25%", position.DisplayValue);
        }

        private IReadOnlyList<HalFeature> features(Device pStaging)
        {
            return mProgrammer.GetStagingFeatures(pStaging);
        }

        private static ScalarCueValue Dimmer(byte pValue)
        {
            return new ScalarCueValue { Feature = "dimmer", Name = "Dimmer", Value = pValue / 255.0 };
        }
    }
}
