using DmxControlUtilities.Lib.Models.Ddf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DmxControlUtilities.Tests
{
    [TestClass]
    public class DdfChannelKeyTests
    {
        [TestMethod]
        public void Rgb_BuildsExpectedKey()
        {
            Assert.AreEqual("rgb/red", DdfChannelKey.Rgb(ColorChannel.Red));
            Assert.AreEqual("rgb/blue/fine", DdfChannelKey.Rgb(ColorChannel.Blue, Resolution.Fine));
        }

        [TestMethod]
        public void CmyAndHsv_BuildExpectedKeys()
        {
            Assert.AreEqual("cmy/cyan", DdfChannelKey.Cmy(CmyChannel.Cyan));
            Assert.AreEqual("hsv/hue", DdfChannelKey.Hsv(HsvChannel.Hue));
        }

        [TestMethod]
        public void PositionAndFunction_BuildExpectedKeys()
        {
            Assert.AreEqual("position/pan", DdfChannelKey.Position(PositionAxis.Pan));
            Assert.AreEqual("position/tilt/fine", DdfChannelKey.Position(PositionAxis.Tilt, Resolution.Fine));
            Assert.AreEqual("dimmer", DdfChannelKey.Function(FunctionChannel.Dimmer));
            Assert.AreEqual("colortemp", DdfChannelKey.Function(FunctionChannel.Colortemp));
        }

        [TestMethod]
        public void TryParse_Rgb()
        {
            Assert.IsTrue(DdfChannelKey.TryParse("rgb/red", out var key));
            Assert.AreEqual(ColorChannel.Red, key.Color);
            Assert.AreEqual(Resolution.Coarse, key.Resolution);
            Assert.IsTrue(key.IsColor);
        }

        [TestMethod]
        public void TryParse_PositionWithFine()
        {
            Assert.IsTrue(DdfChannelKey.TryParse("position/tilt/fine", out var key));
            Assert.AreEqual(PositionAxis.Tilt, key.Position);
            Assert.AreEqual(Resolution.Fine, key.Resolution);
        }

        [TestMethod]
        public void TryParse_CmyAndHsv()
        {
            Assert.IsTrue(DdfChannelKey.TryParse("cmy/magenta", out var cmy));
            Assert.AreEqual(CmyChannel.Magenta, cmy.Cmy);

            Assert.IsTrue(DdfChannelKey.TryParse("hsv/saturation", out var hsv));
            Assert.AreEqual(HsvChannel.Saturation, hsv.Hsv);
        }

        [TestMethod]
        public void TryParse_SimpleFunction()
        {
            Assert.IsTrue(DdfChannelKey.TryParse("dimmer", out var key));
            Assert.AreEqual(FunctionChannel.Dimmer, key.Function);
        }

        [TestMethod]
        public void TryParse_DynamicKeys_ReturnFalse()
        {
            Assert.IsFalse(DdfChannelKey.TryParse("rawstep/Program", out _));
            Assert.IsFalse(DdfChannelKey.TryParse("matrix/0/red", out _));
            Assert.IsFalse(DdfChannelKey.TryParse("radix/0/red", out _));
        }
    }
}
