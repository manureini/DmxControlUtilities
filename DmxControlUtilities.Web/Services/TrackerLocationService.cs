using DmxControlUtilities.Web.Models;
using MathNet.Numerics.LinearAlgebra;

namespace DmxControlUtilities.Web.Services
{
    public class TrackerLocationService
    {
        private readonly DmxControlInstanceService _dmxControlInstanceService;
        public List<TrackerDistances> _trackerDistances = new();

        public TrackerLocationService(DmxControlInstanceService dmxControlInstanceService)
        {
            _dmxControlInstanceService = dmxControlInstanceService;
        }


        public void UpdateDistance(TrackerDistances distances)
        {

            if (distances.Anchor0 == 0)
                return;


            distances.Anchor0 -= 23;
            distances.Anchor1 -= 30;
            distances.Anchor2 -= 30;
            distances.Anchor3 -= 27;

            Console.WriteLine($"{distances.Anchor0}  {distances.Anchor1} {distances.Anchor2} {distances.Anchor3} ");

            int length = 280;


            var anchors = new[]
            {
                Vector<double>.Build.DenseOfArray(new[] {0.0, 0.0, 0.0}),
                Vector<double>.Build.DenseOfArray(new[] {0.0, length, 0.0}),
                Vector<double>.Build.DenseOfArray(new[] { length, length, 0.0}),
                Vector<double>.Build.DenseOfArray(new[] { length, 0.0, 0.0}),
            };

            var distancesArr = new double[]
            {
                distances.Anchor0,
                distances.Anchor1,
                distances.Anchor2,
                distances.Anchor3
            };

            var position = UwbPositionSolver.CalculatePosition(anchors, distancesArr);

            Console.WriteLine($"{distances.Id}  X={position[0]}, Y={position[1]}");

            _ = SendPositionUpdate(position[0] / length, position[1] / length);
        }

        private async Task SendPositionUpdate(double x, double y)
        {
            if (x < 0)
            {
                x = 0;
            }
            if (x > 1)
            {
                x = 1;
            }
            if (y < 0)
            {
                y = 0;
            }
            if (y > 1)
            {
                y = 1;
            }

            Console.WriteLine($"x {x} y {y}");

            y = 1 - y;

            var dmxControlInstance = _dmxControlInstanceService.Instances.First();
            var fixtures = await dmxControlInstance.GetDevices();

            fixtures = fixtures.Where(f => f.Name.Contains("Position")).ToList();

            var targetPosX = x * 360 - 180;
            var targetPosY = y * 360 - 180;

            foreach (var fixture in fixtures)
            {
                _ = dmxControlInstance.UpdateFixture(fixture.Id, (float)targetPosX, (float)targetPosY);
            }
        }



    }
}
