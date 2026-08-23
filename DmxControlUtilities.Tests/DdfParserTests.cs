using DmxControlUtilities.Lib.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Xml.Linq;

namespace DmxControlUtilities.Tests
{
    [TestClass]
    public class DdfParserTests
    {
        private static Lib.Models.Ddf.DeviceDescription Parse(string pXml)
        {
            return DdfParser.Parse(XDocument.Parse(pXml));
        }

        [TestMethod]
        public void ParsesInformationAndChannelCount()
        {
            var d = Parse(@"
                <device dmxaddresscount='10'>
                    <information><model>Par</model><vendor>Shehds</vendor><mode>10 Channel</mode></information>
                    <functions><dimmer dmxchannel='0'/></functions>
                </device>");

            Assert.AreEqual("Par", d.Model);
            Assert.AreEqual("Shehds", d.Vendor);
            Assert.AreEqual("10 Channel", d.Mode);
            Assert.AreEqual(10, d.ChannelCount);
        }

        [TestMethod]
        public void ParsesRgbColorChannels()
        {
            var d = Parse(@"
                <device><functions>
                    <rgb>
                        <red dmxchannel='1'/><green dmxchannel='2'/><blue dmxchannel='3'/>
                        <white dmxchannel='4'/><amber dmxchannel='5'/><uv dmxchannel='6'/>
                    </rgb>
                </functions></device>");

            Assert.IsNotNull(d.GetFunction("rgb/red"));
            Assert.IsNotNull(d.GetFunction("rgb/white"));
            Assert.IsNotNull(d.GetFunction("rgb/amber"));
            Assert.IsNotNull(d.GetFunction("rgb/uv"));
            Assert.AreEqual(4, d.GetFunction("rgb/white")!.DmxChannel);
        }

        [TestMethod]
        public void ParsesShortColorAliases()
        {
            var d = Parse(@"
                <device><functions>
                    <rgb><r dmxchannel='0'/><g dmxchannel='1'/><b dmxchannel='2'/><w dmxchannel='3'/></rgb>
                </functions></device>");

            Assert.IsNotNull(d.GetFunction("rgb/red"));
            Assert.IsNotNull(d.GetFunction("rgb/green"));
            Assert.IsNotNull(d.GetFunction("rgb/blue"));
            Assert.IsNotNull(d.GetFunction("rgb/white"));
        }

        [TestMethod]
        public void ParsesCmyAndHsvAliases()
        {
            var d = Parse(@"
                <device><functions>
                    <cmy><c dmxchannel='0'/><m dmxchannel='1'/><y dmxchannel='2'/></cmy>
                    <hsv><h dmxchannel='3'/><s dmxchannel='4'/><v dmxchannel='5'/></hsv>
                </functions></device>");

            Assert.IsNotNull(d.GetFunction("cmy/cyan"));
            Assert.IsNotNull(d.GetFunction("cmy/magenta"));
            Assert.IsNotNull(d.GetFunction("cmy/yellow"));
            Assert.IsNotNull(d.GetFunction("hsv/hue"));
            Assert.IsNotNull(d.GetFunction("hsv/saturation"));
            Assert.IsNotNull(d.GetFunction("hsv/value"));
        }

        [TestMethod]
        public void ParsesPositionWithFineChannel()
        {
            var d = Parse(@"
                <device><functions>
                    <position>
                        <pan dmxchannel='0' finedmxchannel='1'/>
                        <tilt dmxchannel='2' finedmxchannel='3'/>
                    </position>
                </functions></device>");

            Assert.AreEqual(0, d.GetFunction("position/pan")!.DmxChannel);
            Assert.AreEqual(1, d.GetFunction("position/pan/fine")!.DmxChannel);
            Assert.AreEqual(2, d.GetFunction("position/tilt")!.DmxChannel);
            Assert.AreEqual(3, d.GetFunction("position/tilt/fine")!.DmxChannel);
        }

        [TestMethod]
        public void ParsesRawstepSteps()
        {
            var d = Parse(@"
                <device><functions>
                    <rawstep dmxchannel='8' name='Program'>
                        <step caption='No Function' mindmx='0' maxdmx='50'/>
                        <step caption='Sound Mode' mindmx='251' maxdmx='255'/>
                    </rawstep>
                </functions></device>");

            var f = d.GetFunction("rawstep/Program");
            Assert.IsNotNull(f);
            Assert.AreEqual(2, f.Steps.Count);
            Assert.AreEqual("Sound Mode", f.Steps[1].Caption);
            Assert.AreEqual(251, f.Steps[1].MinDmx);
        }

        [TestMethod]
        public void ParsesInlineMonochromeMatrix()
        {
            var d = Parse(@"<device><functions><matrix dmxchannel='0' monochrome='true' rows='2' columns='2'/></functions></device>");

            Assert.AreEqual(4, d.Functions.Count);
            Assert.AreEqual(0, d.GetFunction("matrix/0/intensity")!.DmxChannel);
            Assert.AreEqual(3, d.GetFunction("matrix/3/intensity")!.DmxChannel);
            Assert.AreEqual(4, d.ChannelCount);
        }

        [TestMethod]
        public void ParsesInlineRgbMatrix()
        {
            var d = Parse(@"<device><functions><matrix dmxchannel='0' rows='1' columns='2'/></functions></device>");

            Assert.AreEqual(6, d.Functions.Count);
            Assert.IsNotNull(d.GetFunction("matrix/0/red"));
            Assert.IsNotNull(d.GetFunction("matrix/1/blue"));
            Assert.AreEqual(5, d.GetFunction("matrix/1/blue")!.DmxChannel);
        }

        [TestMethod]
        public void ParsesRadixRings()
        {
            var d = Parse(@"
                <device><functions>
                    <radix dmxchannel='0'>
                        <ring segments='1'/><ring segments='2'/>
                    </radix>
                </functions></device>");

            // 3 pixels x RGB = 9 channels.
            Assert.AreEqual(9, d.Functions.Count);
            Assert.IsNotNull(d.GetFunction("radix/0/red"));
            Assert.IsNotNull(d.GetFunction("radix/2/blue"));
            Assert.AreEqual(9, d.ChannelCount);
        }

        [TestMethod]
        public void ComputesChannelCountWhenMissing()
        {
            var d = Parse(@"<device><functions><dimmer dmxchannel='4'/></functions></device>");
            Assert.AreEqual(5, d.ChannelCount);
        }

        [TestMethod]
        public void KeepsUnknownFunctionAsGenericChannel()
        {
            var d = Parse(@"<device><functions><somecustom dmxchannel='2' name='Foo'/></functions></device>");
            Assert.AreEqual(1, d.Functions.Count);
            Assert.AreEqual(2, d.Functions[0].DmxChannel);
            Assert.AreEqual("Foo", d.Functions[0].Name);
        }
    }
}
