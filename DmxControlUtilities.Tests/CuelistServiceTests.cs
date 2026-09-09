using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Models.Ddf;
using DmxControlUtilities.Lib.Services;
using DmxControlUtilities.Lib.Services.Hal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;

namespace DmxControlUtilities.Tests
{
    [TestClass]
    public class CuelistServiceTests
    {
        private DmxFtdiService mDmx = null!;
        private DeviceService mDevices = null!;
        private HalService mHal = null!;
        private DeviceDescription mDescription = null!;
        private Device mDevice = null!;
        private CuelistService mService = null!;
        private ManualTimeProvider mClock = null!;

        [TestInitialize]
        public void Initialize()
        {
            mDescription = DdfParser.Parse(XDocument.Parse(@"
                <device><functions>
                    <dimmer dmxchannel='0'/>
                    <rgb><red dmxchannel='1'/><green dmxchannel='2'/><blue dmxchannel='3'/></rgb>
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
            mDevices = new DeviceService(mDmx, new DeviceDescriptionService(mDescription));
            mHal = new HalService(new DeviceDescriptionService(mDescription), mDevices);
            mDevice = mDevices.CreateDevice(mDescription, 1);
            mDevices.AddDevice(mDevice);
            mClock = new ManualTimeProvider();
            mService = new CuelistService(mDevices, mHal, mClock);
        }

        [TestCleanup]
        public void Cleanup()
        {
            mService.StopAll();
            mDmx.Dispose();
        }

        [TestMethod]
        public void Capture_StoresOneTypedValuePerFeature_NotChannels()
        {
            var other = mDevices.CreateDevice(mDescription, 20);
            mDevices.AddDevice(other);
            SetControl("dimmer", 20);
            SetControl("rgb/red", 255);
            SetControl("rgb/blue", 128);
            var list = AddList(CreateDimmerCue(200));
            mService.Go(list.Id);

            var captured = mService.CaptureCue(new[] { mDevice.Id, mDevice.Id }, "Recorded");

            Assert.AreEqual(1, captured.DeviceValues.Count);
            var features = captured.DeviceValues[mDevice.Id];

            // One scalar dimmer feature, one color feature (rgb channels collapsed).
            var dimmer = (ScalarCueValue)features["dimmer"];
            Assert.AreEqual(20 / 255.0, dimmer.Value, 0.01);

            var color = (ColorCueValue)features["rgb"];
            Assert.AreEqual((byte)255, color.R);
            Assert.AreEqual((byte)0, color.G);
            Assert.AreEqual((byte)128, color.B);

            // Captured values are copies: mutating the cue does not touch the device.
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            SetControl("dimmer", 30);
            features["dimmer"] = new ScalarCueValue { Feature = "dimmer", Value = 1 };
            Assert.AreEqual((byte)30, mDevice.GetValue("dimmer"));
        }

        [TestMethod]
        public void Capture_RejectsEmptyOrMissingDevices()
        {
            Assert.ThrowsException<InvalidOperationException>(() => mService.CaptureCue(Array.Empty<Guid>(), "Cue"));
            Assert.ThrowsException<InvalidOperationException>(() => mService.CaptureCue(new[] { Guid.NewGuid() }, "Cue"));
        }

        [TestMethod]
        public void Storage_DeepCopiesOnAddReadAndSave()
        {
            var cue = CreateDimmerCue(10);
            var list = AddList(cue);
            cue.DeviceValues[mDevice.Id]["dimmer"] = new ScalarCueValue { Feature = "dimmer", Value = 99 / 255.0 };
            AssertDimmer(list, 10 / 255.0);

            var copy = mService.GetCuelist(list.Id)!;
            copy.Cues[0].DeviceValues[mDevice.Id]["dimmer"] = new ScalarCueValue { Feature = "dimmer", Value = 66 / 255.0 };
            AssertDimmer(list, 10 / 255.0);
            mService.SaveCuelist(copy);
            copy.Cues[0].DeviceValues[mDevice.Id]["dimmer"] = new ScalarCueValue { Feature = "dimmer", Value = 88 / 255.0 };
            mService.Cuelists[0].Cues.Clear();
            AssertDimmer(list, 66 / 255.0);
        }

        [TestMethod]
        public void EmptyList_DoesNotStart()
        {
            var list = AddList();
            mService.Go(list.Id);
            mService.Back(list.Id);
            mService.Pause(list.Id);
            mService.Resume(list.Id);
            Advance(1000);

            Assert.AreEqual(CuelistPlaybackState.Stopped, mService.GetStatus(list.Id).State);
        }

        [TestMethod]
        public void ManualGo_AdvancesAndHoldsTheLastCue()
        {
            var list = AddList(CreateDimmerCue(10), CreateDimmerCue(200));
            mService.Go(list.Id);
            Advance(10000);
            AssertCue(list, 0);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));

            mService.Go(list.Id);
            mService.Go(list.Id);
            AssertCue(list, 1);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void Follow_WaitsForDelayAndFade_ThenCountsTriggerTime()
        {
            var list = AddList(
                CreateDimmerCue(10, pFade: 500, pDelay: 200),
                CreateDimmerCue(200, pTrigger: CueTrigger.Follow, pTriggerTime: 300));
            mService.Go(list.Id);

            Advance(699);
            AssertCue(list, 0);
            Advance(1);
            AssertCue(list, 0); // fade of cue 1 done, trigger time starts now
            Advance(299);
            AssertCue(list, 0);
            Advance(1);
            AssertCue(list, 1);
        }

        [TestMethod]
        public void Wait_CountsFromTheTriggerOfThePrecedingCue()
        {
            var list = AddList(
                CreateDimmerCue(10, pFade: 5000),
                CreateDimmerCue(200, pTrigger: CueTrigger.Wait, pTriggerTime: 1000));
            mService.Go(list.Id);

            Advance(999);
            AssertCue(list, 0);
            Advance(1);
            AssertCue(list, 1);
        }

        [TestMethod]
        public void PauseAndResume_FreezeAndShiftTheTimeline()
        {
            var list = AddList(
                CreateDimmerCue(10),
                CreateDimmerCue(200, pTrigger: CueTrigger.Wait, pTriggerTime: 1000));
            mService.Go(list.Id);
            Advance(250);

            mService.Pause(list.Id);
            Advance(5000);
            AssertCue(list, 0);
            Assert.AreEqual(CuelistPlaybackState.Paused, mService.GetStatus(list.Id).State);

            mService.Resume(list.Id);
            Advance(750);
            AssertCue(list, 1);
        }

        [TestMethod]
        [Timeout(2000)]
        public void ZeroDurationLoop_IsBoundedAndCanBeStopped()
        {
            var list = AddList(CreateDimmerCue(10, pTrigger: CueTrigger.Follow), CreateDimmerCue(20, pTrigger: CueTrigger.Wait));
            list.Loop = true;
            mService.SaveCuelist(list);
            mService.Go(list.Id);

            for (int i = 0; i < 10; i++)
                mService.Update();

            Assert.IsTrue(mService.GetStatus(list.Id).IsActive);
            mService.Stop(list.Id);
            Assert.AreEqual(CuelistPlaybackState.Stopped, mService.GetStatus(list.Id).State);
            Assert.AreEqual((byte)0, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void Fade_InterpolatesScalarFeatureFromCurrentOutput()
        {
            SetControl("dimmer", 100);
            var list = AddList(CreateDimmerCue(200, pFade: 1000));
            mService.Go(list.Id);
            Advance(500);

            Assert.AreEqual((byte)150, mDmx.GetChannel(1));
            Advance(500);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void Fade_CrossfadesColorInRgbSpace()
        {
            mHal.SetColor(mDevice, 255, 0, 0); // base control: red
            var cue = CreateColorCue(0, 0, 255, pFade: 1000);
            var list = AddList(cue);
            mService.Go(list.Id);
            Advance(500);

            // Midpoint of red->blue crossfade: both channels interpolate, green stays 0.
            Assert.AreEqual((byte)128, mDmx.GetChannel(2));
            Assert.AreEqual((byte)0, mDmx.GetChannel(3));
            Assert.AreEqual((byte)128, mDmx.GetChannel(4));

            Advance(500);
            Assert.AreEqual((byte)0, mDmx.GetChannel(2));
            Assert.AreEqual((byte)255, mDmx.GetChannel(4));
        }

        [TestMethod]
        public void Fade_Interpolates16BitPositionAsOneValue()
        {
            mHal.SetPosition(mDevice, 0, 0.5);
            var cue = CreatePositionCue(1.0, 0.5, pFade: 1000);
            var list = AddList(cue);
            mService.Go(list.Id);
            Advance(500);

            // Pan coarse+fine halfway 0 -> 1.0.
            int pan16 = (mDmx.GetChannel(5) << 8) | mDmx.GetChannel(6);
            Assert.AreEqual(32768, pan16, 512);

            Advance(500);
            pan16 = (mDmx.GetChannel(5) << 8) | mDmx.GetChannel(6);
            Assert.AreEqual(65535, pan16, 512);
        }

        [TestMethod]
        public void DiscreteFeatures_SnapAtFadeCompletion()
        {
            SetControl("rawstep/Program", 10);
            var cue = CreateStepCue(200, pFade: 1000);
            var list = AddList(cue);
            mService.Go(list.Id);
            Advance(999);
            Assert.AreEqual((byte)10, mDmx.GetChannel(9));
            Advance(1);
            Assert.AreEqual((byte)200, mDmx.GetChannel(9));
        }

        [TestMethod]
        public void LatestActivationWins_NotTheLastTimerWriter()
        {
            var older = AddList(CreateDimmerCue(200, pFade: 1000));
            var newer = AddList(CreateDimmerCue(80));
            mService.Go(older.Id);
            Advance(250);
            mService.Go(newer.Id);
            Advance(250);
            Assert.AreEqual((byte)80, mDmx.GetChannel(1));

            mService.Stop(newer.Id);
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));
            Advance(500);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            mService.Stop(older.Id);
            Assert.AreEqual((byte)0, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void DelayedActivation_DoesNotOverrideUntilItsFadeStarts()
        {
            var older = AddList(CreateDimmerCue(160));
            var newer = AddList(CreateDimmerCue(40, pDelay: 100));
            mService.Go(older.Id);
            Advance(10);
            mService.Go(newer.Id);
            Advance(99);
            Assert.AreEqual((byte)160, mDmx.GetChannel(1));
            Advance(1);
            Assert.AreEqual((byte)40, mDmx.GetChannel(1));
            mService.Stop(newer.Id);
            Assert.AreEqual((byte)160, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void NewList_FadesFromCurrentlyVisibleOutput()
        {
            var older = AddList(CreateDimmerCue(200));
            var newer = AddList(CreateDimmerCue(0, pFade: 1000));
            mService.Go(older.Id);
            mService.Go(newer.Id);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            Advance(500);
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));
            mService.Stop(newer.Id);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void NonOverlappingFeatures_MixIndependently()
        {
            var dimmer = AddList(CreateDimmerCue(200));
            var color = AddList(CreateColorCue(150, 0, 0));
            mService.Go(dimmer.Id);
            mService.Go(color.Id);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            Assert.AreEqual((byte)150, mDmx.GetChannel(2));
            mService.Stop(dimmer.Id);
            Assert.AreEqual((byte)0, mDmx.GetChannel(1));
            Assert.AreEqual((byte)150, mDmx.GetChannel(2));
        }

        [TestMethod]
        public void ControlChangesWhilePlaying_AreUsedAfterRelease()
        {
            var list = AddList(CreateDimmerCue(200));
            mService.Go(list.Id);
            SetControl("dimmer", 30);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            mService.Stop(list.Id);
            Assert.AreEqual((byte)30, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void StopAll_ReleasesListsWithoutChangingUnrelatedChannels()
        {
            SetControl("dimmer", 20);
            SetControl("rgb/red", 30);
            mDmx.SetChannel(512, 123);
            var first = AddList(CreateDimmerCue(200));
            var second = AddList(CreateColorCue(150, 0, 0));
            mService.Go(first.Id);
            mService.Go(second.Id);
            mService.StopAll();

            Assert.AreEqual((byte)20, mDmx.GetChannel(1));
            Assert.AreEqual((byte)30, mDmx.GetChannel(2));
            Assert.AreEqual((byte)123, mDmx.GetChannel(512));
            Assert.IsFalse(mService.GetStatus(first.Id).IsActive);
            Assert.IsFalse(mService.GetStatus(second.Id).IsActive);
        }

        [TestMethod]
        public void Back_ReconstructsTrackingAndReleasesLaterOnlyValues()
        {
            var list = AddList(CreateDimmerCue(10), CreateColorCue(0, 0, 200), CreateDimmerCue(90));
            mService.Go(list.Id);
            mService.Go(list.Id);
            mService.Go(list.Id);
            Assert.AreEqual((byte)90, mDmx.GetChannel(1));
            Assert.AreEqual((byte)200, mDmx.GetChannel(4));
            mService.Back(list.Id);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));
            Assert.AreEqual((byte)200, mDmx.GetChannel(4));
            mService.Back(list.Id);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));
            Assert.AreEqual((byte)0, mDmx.GetChannel(4));
        }

        [TestMethod]
        public void Loop_ReleasesValuesThatOnlyOccurInLaterCues()
        {
            var list = AddList(CreateDimmerCue(10), CreateColorCue(0, 0, 200));
            list.Loop = true;
            mService.SaveCuelist(list);
            mService.Go(list.Id);
            mService.Go(list.Id);
            Assert.AreEqual((byte)200, mDmx.GetChannel(4));
            mService.Go(list.Id);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));
            Assert.AreEqual((byte)0, mDmx.GetChannel(4));
        }

        [TestMethod]
        public void MissingAndRemovedDevices_DoNotReceivePlaybackWrites()
        {
            var cue = CreateDimmerCue(50);
            cue.DeviceValues[Guid.NewGuid()] = new Dictionary<string, CueFeatureValue>
            {
                ["dimmer"] = new ScalarCueValue { Feature = "dimmer", Name = "Dimmer", Value = 200 / 255.0 }
            };
            var list = AddList(cue);
            mService.Go(list.Id);
            Assert.AreEqual((byte)50, mDmx.GetChannel(1));
            mDevices.RemoveDevice(mDevice);
            mDmx.SetChannel(1, 17);
            Advance(1000);
            Assert.AreEqual((byte)17, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void EditingAnActiveList_IsRejected_AndDeletionReleasesIt()
        {
            var list = AddList(CreateDimmerCue(200));
            mService.Go(list.Id);
            list.Cues[0].DeviceValues[mDevice.Id]["dimmer"] = new ScalarCueValue { Feature = "dimmer", Value = 100 / 255.0 };
            Assert.ThrowsException<InvalidOperationException>(() => mService.SaveCuelist(list));
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            mService.RemoveCuelist(list.Id);

            Assert.IsNull(mService.GetCuelist(list.Id));
            Assert.AreEqual(CuelistPlaybackState.Stopped, mService.GetStatus(list.Id).State);
            Assert.AreEqual((byte)0, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void SavedReordering_IsUsedOnTheNextStart()
        {
            var list = AddList(CreateDimmerCue(10), CreateDimmerCue(20));
            list.Name = "Renamed";
            list.Cues.Reverse();
            mService.SaveCuelist(list);
            mService.Go(list.Id);

            Assert.AreEqual("Renamed", mService.GetCuelist(list.Id)!.Name);
            Assert.AreEqual((byte)20, mDmx.GetChannel(1));
        }

        [DataTestMethod]
        [DataRow(-1, 0, 0)]
        [DataRow(0, -1, 0)]
        [DataRow(0, 0, -1)]
        public void Validation_RejectsNegativeTimings(int pFade, int pDelay, int pTriggerTime)
        {
            var cue = CreateDimmerCue(100, pFade: pFade, pDelay: pDelay, pTriggerTime: pTriggerTime);
            Assert.ThrowsException<ValidationException>(() => AddList(cue));
            Assert.AreEqual(0, mService.Cuelists.Count);
        }

        [TestMethod]
        public void Validation_RejectsUnsupportedTriggersDuplicateCueIdsAndEmptyNames()
        {
            var cue = CreateDimmerCue(100, pTrigger: (CueTrigger)999);
            Assert.ThrowsException<ValidationException>(() => AddList(cue));
            cue.Trigger = CueTrigger.Manual;
            Assert.ThrowsException<ValidationException>(() => AddList(cue, cue.Clone()));
            cue.Name = " ";
            Assert.ThrowsException<ValidationException>(() => AddList(cue));
            Assert.ThrowsException<ValidationException>(() => mService.AddCuelist(new Cuelist { Name = " " }));
        }

        private void AssertDimmer(Cuelist pList, double pExpected)
        {
            var value = (ScalarCueValue)mService.GetCuelist(pList.Id)!.Cues[0].DeviceValues[mDevice.Id]["dimmer"];
            Assert.AreEqual(pExpected, value.Value, 0.01);
        }

        private Cue CreateDimmerCue(byte pValue, int pFade = 0, int pDelay = 0,
            CueTrigger pTrigger = CueTrigger.Manual, int pTriggerTime = 0)
        {
            return CreateCue(new ScalarCueValue { Feature = "dimmer", Name = "Dimmer", Value = pValue / 255.0 },
                pFade, pDelay, pTrigger, pTriggerTime);
        }

        private Cue CreateColorCue(byte pR, byte pG, byte pB, int pFade = 0)
        {
            return CreateCue(new ColorCueValue { Feature = "rgb", Name = "Color", R = pR, G = pG, B = pB }, pFade, 0);
        }

        private Cue CreatePositionCue(double pPan, double pTilt, int pFade = 0)
        {
            return CreateCue(new PositionCueValue { Feature = "position", Name = "Position", Pan = pPan, Tilt = pTilt }, pFade, 0);
        }

        private Cue CreateStepCue(byte pDmxValue, int pFade = 0)
        {
            // Program steps: 0..127 = Off, 128..255 = On; normalize dmx value to 0..1.
            return CreateCue(new ScalarCueValue
            {
                Feature = "rawstep/Program",
                Name = "Program",
                IsDiscrete = true,
                Value = pDmxValue / 255.0
            }, pFade, 0);
        }

        private Cue CreateCue(CueFeatureValue pValue, int pFade = 0, int pDelay = 0,
            CueTrigger pTrigger = CueTrigger.Manual, int pTriggerTime = 0)
        {
            return new Cue
            {
                Name = "Cue",
                FadeMilliseconds = pFade,
                DelayMilliseconds = pDelay,
                Trigger = pTrigger,
                TriggerMilliseconds = pTriggerTime,
                DeviceValues = new Dictionary<Guid, Dictionary<string, CueFeatureValue>>
                {
                    [mDevice.Id] = new Dictionary<string, CueFeatureValue> { [pValue.Feature] = pValue }
                }
            };
        }

        private Cuelist AddList(params Cue[] pCues)
        {
            var list = new Cuelist { Name = "List " + (mService.Cuelists.Count + 1), Cues = pCues.ToList() };
            mService.AddCuelist(list);
            return list;
        }

        private void SetControl(string pKey, byte pValue)
        {
            mDevice.SetValue(pKey, pValue);
            mDevices.ApplyDevice(mDevice);
        }

        private void Advance(int pMilliseconds)
        {
            mClock.Advance(pMilliseconds);
            mService.Update();
        }

        private void AssertCue(Cuelist pList, int pIndex)
        {
            Assert.AreEqual((int?)pIndex, mService.GetStatus(pList.Id).CurrentCueIndex);
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private long mTimestamp;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => mTimestamp;

            public void Advance(int pMilliseconds)
            {
                mTimestamp += TimeSpan.FromMilliseconds(pMilliseconds).Ticks;
            }
        }
    }
}
