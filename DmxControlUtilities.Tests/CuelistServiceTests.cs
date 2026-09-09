using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Models.Ddf;
using DmxControlUtilities.Lib.Services;
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
                    <rgb><red dmxchannel='1'/></rgb>
                    <position>
                        <pan dmxchannel='2' finedmxchannel='3'/>
                        <tilt dmxchannel='4' finedmxchannel='5' ultradmxchannel='6' ultrafinedmxchannel='7'/>
                    </position>
                    <rawstep name='Program' dmxchannel='8'>
                        <step type='value' val='0' mindmx='0' maxdmx='127' caption='Off'/>
                        <step type='value' val='1' mindmx='128' maxdmx='255' caption='On'/>
                    </rawstep>
                </functions></device>"));
            mDescription.Id = "test";
            mDmx = new DmxFtdiService();
            mDevices = new DeviceService(mDmx, new DeviceDescriptionService(mDescription));
            mDevice = mDevices.CreateDevice(mDescription, 1);
            mDevices.AddDevice(mDevice);
            mClock = new ManualTimeProvider();
            mService = new CuelistService(mDevices, mClock);
        }

        [TestCleanup]
        public void Cleanup()
        {
            mService.StopAll();
            mDmx.Dispose();
        }

        [TestMethod]
        public void Capture_CopiesOnlySelectedDeviceControls_NotPlaybackOutput()
        {
            var other = mDevices.CreateDevice(mDescription, 20);
            mDevices.AddDevice(other);
            SetControl("dimmer", 20);
            var list = AddList(CreateCue(200));
            mService.Go(list.Id);

            var captured = mService.CaptureCue(new[] { mDevice.Id, mDevice.Id }, "Recorded");

            Assert.AreEqual(1, captured.DeviceValues.Count);
            Assert.AreEqual((byte)20, captured.DeviceValues[mDevice.Id]["dimmer"]);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            SetControl("dimmer", 30);
            Assert.AreEqual((byte)20, captured.DeviceValues[mDevice.Id]["dimmer"]);
            captured.DeviceValues[mDevice.Id]["dimmer"] = 99;
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
            var cue = CreateCue(10);
            var list = AddList(cue);
            cue.DeviceValues[mDevice.Id]["dimmer"] = 99;
            Assert.AreEqual((byte)10, mService.GetCuelist(list.Id)!.Cues[0].DeviceValues[mDevice.Id]["dimmer"]);

            var copy = mService.GetCuelist(list.Id)!;
            copy.Cues[0].DeviceValues[mDevice.Id]["dimmer"] = 66;
            Assert.AreEqual((byte)10, mService.GetCuelist(list.Id)!.Cues[0].DeviceValues[mDevice.Id]["dimmer"]);
            mService.SaveCuelist(copy);
            copy.Cues[0].DeviceValues[mDevice.Id]["dimmer"] = 88;
            mService.Cuelists[0].Cues.Clear();
            Assert.AreEqual((byte)66, mService.GetCuelist(list.Id)!.Cues[0].DeviceValues[mDevice.Id]["dimmer"]);
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
            var list = AddList(CreateCue(10), CreateCue(200));
            mService.Go(list.Id);
            Advance(10000);
            AssertCue(list, 0);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));

            mService.Go(list.Id);
            mService.Go(list.Id);
            AssertCue(list, 1);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            mService.Back(list.Id);
            mService.Back(list.Id);
            AssertCue(list, 0);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void StopAndRestart_StartsAtTheFirstCue()
        {
            var list = AddList(CreateCue(80), CreateCue(100));
            mService.Go(list.Id);
            mService.Go(list.Id);
            mService.Stop(list.Id);
            Assert.AreEqual((byte)0, mDmx.GetChannel(1));

            mService.Go(list.Id);
            AssertCue(list, 0);
            Assert.AreEqual((byte)80, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void ExplicitGo_OverridesTheNextAutomaticTrigger()
        {
            var list = AddList(CreateCue(10), CreateCue(200, pTrigger: CueTrigger.Follow, pTriggerTime: 10000));
            mService.Go(list.Id);
            Advance(100);
            mService.Go(list.Id);

            AssertCue(list, 1);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void DelayAndFade_InterpolateWithoutMutatingControls()
        {
            SetControl("dimmer", 20);
            var list = AddList(CreateCue(220, pFade: 1000, pDelay: 200));
            mService.Go(list.Id);
            Assert.AreEqual(CuelistPlaybackState.Delaying, mService.GetStatus(list.Id).State);
            Advance(199);
            Assert.AreEqual((byte)20, mDmx.GetChannel(1));
            Assert.AreEqual(1, mService.GetStatus(list.Id).RemainingDelayMilliseconds);
            Advance(1);
            Assert.AreEqual(CuelistPlaybackState.Fading, mService.GetStatus(list.Id).State);
            Advance(500);
            Assert.AreEqual((byte)120, mDmx.GetChannel(1));
            Advance(500);
            Assert.AreEqual((byte)220, mDmx.GetChannel(1));
            Assert.AreEqual(CuelistPlaybackState.Holding, mService.GetStatus(list.Id).State);
            Assert.AreEqual((byte)20, mDevice.GetValue("dimmer"));
        }

        [TestMethod]
        public void Follow_WaitsForPreviousDelayFadeAndItsOwnTriggerTime()
        {
            var list = AddList(CreateCue(100, pFade: 400, pDelay: 100),
                CreateCue(200, pDelay: 50, pTrigger: CueTrigger.Follow, pTriggerTime: 200));
            mService.Go(list.Id);
            Advance(699);
            AssertCue(list, 0);
            Advance(1);
            AssertCue(list, 1);
            Assert.AreEqual(CuelistPlaybackState.Delaying, mService.GetStatus(list.Id).State);
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));
            Advance(49);
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));
            Advance(1);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void Wait_CountsFromPreviousTrigger_NotFadeCompletion()
        {
            var list = AddList(CreateCue(200, pFade: 1000, pDelay: 100),
                CreateCue(100, pTrigger: CueTrigger.Wait, pTriggerTime: 300));
            mService.Go(list.Id);
            Advance(299);
            AssertCue(list, 0);
            Advance(1);
            AssertCue(list, 1);
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void Wait_CanReplaceACueBeforeItsDelayExpires()
        {
            var list = AddList(CreateCue(200, pDelay: 1000),
                CreateCue(100, pTrigger: CueTrigger.Wait, pTriggerTime: 100));
            mService.Go(list.Id);
            Advance(100);
            AssertCue(list, 1);
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));
            Advance(2000);
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void Pause_FreezesFadeAndFollowCountdown()
        {
            var list = AddList(CreateCue(200, pFade: 1000),
                CreateCue(100, pTrigger: CueTrigger.Follow, pTriggerTime: 500));
            mService.Go(list.Id);
            Advance(250);
            mService.Pause(list.Id);
            Advance(5000);
            Assert.AreEqual((byte)50, mDmx.GetChannel(1));
            Assert.AreEqual(CuelistPlaybackState.Paused, mService.GetStatus(list.Id).State);
            Assert.AreEqual(750, mService.GetStatus(list.Id).RemainingFadeMilliseconds);
            Assert.AreEqual((int?)1250, mService.GetStatus(list.Id).NextCueMilliseconds);

            mService.Resume(list.Id);
            Advance(750);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            Advance(499);
            AssertCue(list, 0);
            Advance(1);
            AssertCue(list, 1);
        }

        [TestMethod]
        public void GoWhilePaused_ResumesWithoutAdvancing()
        {
            var list = AddList(CreateCue(100, pFade: 1000), CreateCue(200));
            mService.Go(list.Id);
            Advance(250);
            mService.Pause(list.Id);
            Advance(1000);
            mService.Go(list.Id);
            AssertCue(list, 0);
            Assert.AreEqual((byte)25, mDmx.GetChannel(1));
            Advance(750);
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void PauseDuringDelay_PreservesRemainingDelay()
        {
            var list = AddList(CreateCue(200, pDelay: 500, pFade: 500));
            mService.Go(list.Id);
            Advance(200);
            mService.Pause(list.Id);
            Advance(1000);
            Assert.AreEqual(300, mService.GetStatus(list.Id).RemainingDelayMilliseconds);
            Assert.AreEqual((byte)0, mDmx.GetChannel(1));
            mService.Resume(list.Id);
            Advance(300);
            Assert.AreEqual(CuelistPlaybackState.Fading, mService.GetStatus(list.Id).State);
            Advance(500);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void InterruptedFade_StartsFromCurrentOutput()
        {
            var list = AddList(CreateCue(200, pFade: 1000), CreateCue(0, pFade: 1000));
            mService.Go(list.Id);
            Advance(250);
            Assert.AreEqual((byte)50, mDmx.GetChannel(1));
            mService.Go(list.Id);
            Assert.AreEqual((byte)50, mDmx.GetChannel(1));
            Advance(500);
            Assert.AreEqual((byte)25, mDmx.GetChannel(1));
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(250)]
        public void Stop_CancelsDelayedAndFadingOutput(int pElapsed)
        {
            SetControl("dimmer", 44);
            var list = AddList(CreateCue(200, pDelay: 100, pFade: 1000));
            mService.Go(list.Id);
            Advance(pElapsed);
            mService.Stop(list.Id);
            Advance(5000);

            Assert.AreEqual(CuelistPlaybackState.Stopped, mService.GetStatus(list.Id).State);
            Assert.AreEqual((byte)44, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void ManualLoop_WrapsGoAndBack()
        {
            var list = AddList(CreateCue(10), CreateCue(20));
            list.Loop = true;
            mService.SaveCuelist(list);
            mService.Go(list.Id);
            mService.Go(list.Id);
            mService.Go(list.Id);
            AssertCue(list, 0);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));
            mService.Back(list.Id);
            AssertCue(list, 1);
            Assert.AreEqual((byte)20, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void AutomaticLoop_UsesTheFirstCuesTriggerOnWrap()
        {
            var list = AddList(CreateCue(10, pTrigger: CueTrigger.Follow, pTriggerTime: 100),
                CreateCue(20, pTrigger: CueTrigger.Follow, pTriggerTime: 200));
            list.Loop = true;
            mService.SaveCuelist(list);
            mService.Go(list.Id);
            Advance(200);
            AssertCue(list, 1);
            Advance(99);
            AssertCue(list, 1);
            Advance(1);
            AssertCue(list, 0);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));
            Advance(200);
            AssertCue(list, 1);
        }

        [TestMethod]
        [Timeout(2000)]
        public void ZeroDurationLoop_IsBoundedAndCanBeStopped()
        {
            var list = AddList(CreateCue(10, pTrigger: CueTrigger.Follow), CreateCue(20, pTrigger: CueTrigger.Wait));
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
        public void LatestActivationWins_NotTheLastTimerWriter()
        {
            var older = AddList(CreateCue(200, pFade: 1000));
            var newer = AddList(CreateCue(80));
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
            var older = AddList(CreateCue(160));
            var newer = AddList(CreateCue(40, pDelay: 100));
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
            var older = AddList(CreateCue(200));
            var newer = AddList(CreateCue(0, pFade: 1000));
            mService.Go(older.Id);
            mService.Go(newer.Id);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            Advance(500);
            Assert.AreEqual((byte)100, mDmx.GetChannel(1));
            mService.Stop(newer.Id);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
        }

        [TestMethod]
        public void NonOverlappingFunctions_MixIndependently()
        {
            var dimmer = AddList(CreateCue(200));
            var red = AddList(CreateCue(150, "rgb/red"));
            mService.Go(dimmer.Id);
            mService.Go(red.Id);
            Assert.AreEqual((byte)200, mDmx.GetChannel(1));
            Assert.AreEqual((byte)150, mDmx.GetChannel(2));
            mService.Stop(dimmer.Id);
            Assert.AreEqual((byte)0, mDmx.GetChannel(1));
            Assert.AreEqual((byte)150, mDmx.GetChannel(2));
        }

        [TestMethod]
        public void ControlChangesWhilePlaying_AreUsedAfterRelease()
        {
            var list = AddList(CreateCue(200));
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
            var first = AddList(CreateCue(200));
            var second = AddList(CreateCue(150, "rgb/red"));
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
            var list = AddList(CreateCue(10), CreateCue(200, "rgb/red"), CreateCue(90));
            mService.Go(list.Id);
            mService.Go(list.Id);
            mService.Go(list.Id);
            Assert.AreEqual((byte)90, mDmx.GetChannel(1));
            Assert.AreEqual((byte)200, mDmx.GetChannel(2));
            mService.Back(list.Id);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));
            Assert.AreEqual((byte)200, mDmx.GetChannel(2));
            mService.Back(list.Id);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));
            Assert.AreEqual((byte)0, mDmx.GetChannel(2));
        }

        [TestMethod]
        public void Loop_ReleasesValuesThatOnlyOccurInLaterCues()
        {
            var list = AddList(CreateCue(10), CreateCue(200, "rgb/red"));
            list.Loop = true;
            mService.SaveCuelist(list);
            mService.Go(list.Id);
            mService.Go(list.Id);
            Assert.AreEqual((byte)200, mDmx.GetChannel(2));
            mService.Go(list.Id);
            Assert.AreEqual((byte)10, mDmx.GetChannel(1));
            Assert.AreEqual((byte)0, mDmx.GetChannel(2));
        }

        [TestMethod]
        public void Fade_CombinesCoarseAndFineBytes()
        {
            SetControl("position/pan", 0);
            SetControl("position/pan/fine", 255);
            var cue = CreateCue(1, "position/pan", pFade: 1000);
            cue.DeviceValues[mDevice.Id]["position/pan/fine"] = 0;
            var list = AddList(cue);
            mService.Go(list.Id);
            Advance(500);

            Assert.AreEqual(256, (mDmx.GetChannel(3) << 8) | mDmx.GetChannel(4));
            Assert.AreEqual((byte)255, mDevice.GetValue("position/pan/fine"));
        }

        [TestMethod]
        public void Fade_CombinesAllFourResolutionBytes()
        {
            SetControl("position/tilt", 0);
            SetControl("position/tilt/fine", 255);
            SetControl("position/tilt/ultra", 255);
            SetControl("position/tilt/ultrafine", 255);
            var cue = CreateCue(1, "position/tilt", pFade: 1000);
            cue.DeviceValues[mDevice.Id]["position/tilt/fine"] = 0;
            cue.DeviceValues[mDevice.Id]["position/tilt/ultra"] = 0;
            cue.DeviceValues[mDevice.Id]["position/tilt/ultrafine"] = 0;
            var list = AddList(cue);
            mService.Go(list.Id);
            Advance(500);

            uint value = ((uint)mDmx.GetChannel(5) << 24) | ((uint)mDmx.GetChannel(6) << 16)
                | ((uint)mDmx.GetChannel(7) << 8) | mDmx.GetChannel(8);
            Assert.AreEqual(0x01000000u, value);
        }

        [TestMethod]
        public void DiscreteFunctions_SnapAtFadeCompletion()
        {
            string key = mDescription.Functions.Single(f => f.Steps.Count > 0).Key;
            SetControl(key, 10);
            var list = AddList(CreateCue(200, key, pFade: 1000));
            mService.Go(list.Id);
            Advance(999);
            Assert.AreEqual((byte)10, mDmx.GetChannel(9));
            Advance(1);
            Assert.AreEqual((byte)200, mDmx.GetChannel(9));
        }

        [TestMethod]
        public void MissingAndRemovedDevices_DoNotReceivePlaybackWrites()
        {
            var cue = CreateCue(50);
            cue.DeviceValues[Guid.NewGuid()] = new Dictionary<string, byte> { ["dimmer"] = 200 };
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
            var list = AddList(CreateCue(200));
            mService.Go(list.Id);
            list.Cues[0].DeviceValues[mDevice.Id]["dimmer"] = 100;
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
            var list = AddList(CreateCue(10), CreateCue(20));
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
            var cue = CreateCue(100, pFade: pFade, pDelay: pDelay, pTriggerTime: pTriggerTime);
            Assert.ThrowsException<ValidationException>(() => AddList(cue));
            Assert.AreEqual(0, mService.Cuelists.Count);
        }

        [TestMethod]
        public void Validation_RejectsUnsupportedTriggersDuplicateCueIdsAndEmptyNames()
        {
            var cue = CreateCue(100, pTrigger: (CueTrigger)999);
            Assert.ThrowsException<ValidationException>(() => AddList(cue));
            cue.Trigger = CueTrigger.Manual;
            Assert.ThrowsException<ValidationException>(() => AddList(cue, cue.Clone()));
            cue.Name = " ";
            Assert.ThrowsException<ValidationException>(() => AddList(cue));
            Assert.ThrowsException<ValidationException>(() => mService.AddCuelist(new Cuelist { Name = " " }));
        }

        private Cue CreateCue(byte pValue, string pKey = "dimmer", int pFade = 0, int pDelay = 0,
            CueTrigger pTrigger = CueTrigger.Manual, int pTriggerTime = 0)
        {
            return new Cue
            {
                Name = "Cue",
                FadeMilliseconds = pFade,
                DelayMilliseconds = pDelay,
                Trigger = pTrigger,
                TriggerMilliseconds = pTriggerTime,
                DeviceValues = new Dictionary<Guid, Dictionary<string, byte>>
                {
                    [mDevice.Id] = new Dictionary<string, byte> { [pKey] = pValue }
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
