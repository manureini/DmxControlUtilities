using DmxControlUtilities.Web.Models;

namespace DmxControlUtilities.Web.Services
{
    public class FixtureService
    {
        protected DmxControlInstanceService _dmxControlInstanceService;

        public FixtureService(DmxControlInstanceService dmxControlInstanceService)
        {
            _dmxControlInstanceService = dmxControlInstanceService;

            Fixtures.Add(new Fixture()
            {
                X = 3,
                Y = 8,
                Z = 3
            });
        }

        public List<Fixture> Fixtures { get; set; } = new();

        public async Task SendUpdate(string fixtureId, float yaw, float pitch)
        {


            var instance = _dmxControlInstanceService.Instances.First();


            await instance.UpdateFixture(fixtureId, yaw, pitch);






        }




    }
}
