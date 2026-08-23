using DmxControlUtilities.Lib.Models;
using DmxControlUtilities.Lib.Models.Ddf;
using DmxControlUtilities.Lib.Services;
using DmxControlUtilities.Lib.Services.Hal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Xml.Linq;

namespace DmxControlUtilities.Tests
{
    [TestClass]
    public class HalServiceTests
    {
        private static HalService CreateHal(string pXml, out Device pDevice)
        {
            var description = DdfParser.Parse(XDocument.Parse(pXml));
            description.Id = "test";

            var hal = new HalService(new DeviceDescriptionService(description));

            pDevice = new Device { DescriptionId = description.Id };

            foreach (var function in description.Functions)
                pDevice.SetValue(function.Key, function.DefaultValue);

            return hal;
        }

        [TestMethod]
        public void SetColor_Rgb_WritesDirectly()
        {
            var hal = CreateHal(@"<device><functions><rgb><red dmxchannel='0'/><green dmxchannel='1'/><blue dmxchannel='2'/></rgb></functions></device>", out var device);

            hal.SetColor(device, 10, 20, 200);

            Assert.AreEqual((byte)10, device.GetValue("rgb/red"));
            Assert.AreEqual((byte)20, device.GetValue("rgb/green"));
            Assert.AreEqual((byte)200, device.GetValue("rgb/blue"));
            Assert.AreEqual(((byte)10, (byte)20, (byte)200), hal.GetColor(device));
        }

        [TestMethod]
        public void SetColor_AddWhite_KeepsRgbAndAddsWhite()
        {
            var hal = CreateHal(@"<device whitechanneldefaultmode='addwhite'><functions><rgb><red dmxchannel='0'/><green dmxchannel='1'/><blue dmxchannel='2'/><white dmxchannel='3'/></rgb></functions></device>", out var device);

            hal.SetColor(device, 255, 255, 255);

            Assert.AreEqual((byte)255, device.GetValue("rgb/red"));
            Assert.AreEqual((byte)255, device.GetValue("rgb/white"));
        }

        [TestMethod]
        public void SetColor_OnlyWhite_SubtractsWhiteFromRgb()
        {
            var hal = CreateHal(@"<device whitechanneldefaultmode='onlywhite'><functions><rgb><red dmxchannel='0'/><green dmxchannel='1'/><blue dmxchannel='2'/><white dmxchannel='3'/></rgb></functions></device>", out var device);

            hal.SetColor(device, 255, 255, 255);

            Assert.AreEqual((byte)0, device.GetValue("rgb/red"));
            Assert.AreEqual((byte)255, device.GetValue("rgb/white"));
        }

        [TestMethod]
        public void SetColor_NoWhiteMode_LeavesWhiteOff()
        {
            var hal = CreateHal(@"<device><functions><rgb><red dmxchannel='0'/><green dmxchannel='1'/><blue dmxchannel='2'/><white dmxchannel='3'/></rgb></functions></device>", out var device);

            hal.SetColor(device, 255, 255, 255);

            Assert.AreEqual((byte)0, device.GetValue("rgb/white"));
        }

        [TestMethod]
        public void SetColor_AddAmber_DrivesAmberNearYellow()
        {
            var hal = CreateHal(@"<device amberchanneldefaultmode='add'><functions><rgb><red dmxchannel='0'/><green dmxchannel='1'/><blue dmxchannel='2'/><amber dmxchannel='3'/></rgb></functions></device>", out var device);

            hal.SetColor(device, 255, 128, 0); // orange-ish, hue ~30 -> within amber trapezoid

            Assert.IsTrue(device.GetValue("rgb/amber") > 0);

            hal.SetColor(device, 0, 0, 255); // blue, hue 240 -> outside amber trapezoid

            Assert.AreEqual((byte)0, device.GetValue("rgb/amber"));
        }

        [TestMethod]
        public void SetColor_Cmy_ConvertsSubtractive()
        {
            var hal = CreateHal(@"<device><functions><cmy><cyan dmxchannel='0'/><magenta dmxchannel='1'/><yellow dmxchannel='2'/></cmy></functions></device>", out var device);

            hal.SetColor(device, 255, 0, 0); // red = cyan 0, magenta 1, yellow 1

            Assert.AreEqual((byte)0, device.GetValue("cmy/cyan"));
            Assert.AreEqual((byte)255, device.GetValue("cmy/magenta"));
            Assert.AreEqual((byte)255, device.GetValue("cmy/yellow"));
            Assert.AreEqual(((byte)255, (byte)0, (byte)0), hal.GetColor(device));
        }

        [TestMethod]
        public void SetColor_Hsv_ConvertsHue()
        {
            var hal = CreateHal(@"<device><functions><hsv><h dmxchannel='0'/><s dmxchannel='1'/><v dmxchannel='2'/></hsv></functions></device>", out var device);

            hal.SetColor(device, 0, 255, 0); // green = hue 120

            Assert.AreEqual(120, (int)System.Math.Round(device.GetValue("hsv/hue") / 255.0 * 360.0));
            Assert.AreEqual((byte)255, device.GetValue("hsv/saturation"));
            Assert.AreEqual((byte)255, device.GetValue("hsv/value"));

            var (r, g, b) = hal.GetColor(device);
            Assert.AreEqual(0, r);
            Assert.AreEqual(255, g);
            Assert.AreEqual(0, b);
        }

        [TestMethod]
        public void SetColor_ColorWheel_PicksNearestStep()
        {
            var hal = CreateHal(@"
                <device><functions>
                    <colorwheel dmxchannel='0'>
                        <step type='color' val='#ff0000' mindmx='10' maxdmx='19' caption='Red'/>
                        <step type='color' val='#0000ff' mindmx='30' maxdmx='39' caption='Blue'/>
                    </colorwheel>
                </functions></device>", out var device);

            hal.SetColor(device, 5, 5, 250); // near blue

            Assert.AreEqual((byte)30, device.GetValue("colorwheel"));
        }

        [TestMethod]
        public void SetDimmer_UsesHardwareChannel()
        {
            var hal = CreateHal(@"<device><functions><dimmer dmxchannel='0'/></functions></device>", out var device);

            hal.SetDimmer(device, 0.5);

            Assert.AreEqual((byte)128, device.GetValue("dimmer"));
            Assert.AreEqual(0.5, hal.GetDimmer(device), 0.01);
        }

        [TestMethod]
        public void SetPosition_Distributes16Bit()
        {
            var hal = CreateHal(@"
                <device><functions>
                    <position><pan dmxchannel='0' finedmxchannel='1'/><tilt dmxchannel='2' finedmxchannel='3'/></position>
                </functions></device>", out var device);

            hal.SetPosition(device, 1.0, 0.25);

            Assert.AreEqual((byte)255, device.GetValue("position/pan"));
            Assert.AreEqual((byte)64, device.GetValue("position/tilt"));

            var (pan, tilt) = hal.GetPosition(device);
            Assert.AreEqual(1.0, pan, 0.01);
            Assert.AreEqual(0.25, tilt, 0.02);
        }

        [TestMethod]
        public void SetColorTemperature_MapsToRange()
        {
            var hal = CreateHal(@"
                <device><functions>
                    <colortemp dmxchannel='0'><range mindmx='0' maxdmx='255' minval='2800' maxval='6500'/></colortemp>
                </functions></device>", out var device);

            hal.SetColorTemperature(device, 6500);

            Assert.AreEqual((byte)255, device.GetValue("colortemp"));
        }

        [TestMethod]
        public void SetValue_RgbKey_RoutesThroughColor()
        {
            var hal = CreateHal(@"<device><functions><rgb><red dmxchannel='0'/><green dmxchannel='1'/><blue dmxchannel='2'/></rgb></functions></device>", out var device);

            hal.SetValue(device, DdfChannelKey.Rgb(ColorChannel.Red), 200);

            Assert.AreEqual((byte)200, device.GetValue("rgb/red"));
        }

        [TestMethod]
        public void SetValue_CmyKey_ConvertsFromRgb()
        {
            var hal = CreateHal(@"<device><functions><cmy><cyan dmxchannel='0'/><magenta dmxchannel='1'/><yellow dmxchannel='2'/></cmy></functions></device>", out var device);

            // Device defaults cyan/magenta/yellow to 0 (white). Setting magenta to full
            // yields cmy(0,255,0) = rgb(255,0,255) = magenta.
            hal.SetValue(device, DdfChannelKey.Cmy(CmyChannel.Magenta), 255);

            var (r, g, b) = hal.GetColor(device);
            Assert.AreEqual(255, r);
            Assert.AreEqual(0, g);
            Assert.AreEqual(255, b);
        }

        [TestMethod]
        public void SetValue_DimmerKey_RoutesThroughDimmer()
        {
            var hal = CreateHal(@"<device><functions><dimmer dmxchannel='0'/></functions></device>", out var device);

            hal.SetValue(device, DdfChannelKey.Function(FunctionChannel.Dimmer), 128);

            Assert.AreEqual(0.5, hal.GetDimmer(device), 0.01);
        }

        [TestMethod]
        public void SetValue_PositionKey_RoutesThroughPosition()
        {
            var hal = CreateHal(@"
                <device><functions>
                    <position><pan dmxchannel='0' finedmxchannel='1'/><tilt dmxchannel='2' finedmxchannel='3'/></position>
                </functions></device>", out var device);

            hal.SetValue(device, DdfChannelKey.Position(PositionAxis.Pan), 255);

            Assert.AreEqual(1.0, hal.GetPosition(device).Pan, 0.01);
            Assert.AreEqual((byte)255, device.GetValue("position/pan"));
        }

        [TestMethod]
        public void SetValue_DynamicKey_AppliedRaw()
        {
            var hal = CreateHal(@"<device><functions><rawstep dmxchannel='0' name='Program'>
                <step caption='A' mindmx='0' maxdmx='100'/></rawstep></functions></device>", out var device);

            hal.SetValue(device, "rawstep/Program", 50);

            Assert.AreEqual((byte)50, device.GetValue("rawstep/Program"));
        }
    }
}
