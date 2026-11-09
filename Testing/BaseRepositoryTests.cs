using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.DB.Repositories;
using Microsoft.EntityFrameworkCore;


namespace Testing
{
    [TestFixture]
    public class BaseRepositoryTests
    {
        private static ApplicationContext context;
        static DateTime now = DateTime.UtcNow;
        static string by = "Test";

        static Item SetTimbales = new Item
        {
            UpdatedAt = now,
            UpdatedBy = by,
            Id = 1,
            Label = "Timbal Tito Puente",
            Attributes = "",
            ParentId = null,
            IsActive = true,
        };
        static Item Timbal14 = new Item
        {
            UpdatedAt = now,
            UpdatedBy = by,
            Id = 2,
            Label = "Timbal 14\"",
            Attributes = "",
            ParentId = 1,
            IsActive = true,
        };
        static Item Aro14 = new Item
        {
            UpdatedAt = now,
            UpdatedBy = by,
            Id = 3,
            Label = "Aro Timbal 14\"",
            Attributes = "",
            ParentId = 2,
            IsActive = true,
        };
        static Item Tornillo = new Item
        {
            UpdatedAt = now,
            UpdatedBy = by,
            Id = 4,
            Label = "Tornillos",
            Attributes = "",
            ParentId = 2,
            IsActive = true,
        };
        static Item Timbal15 = new Item
        {
            UpdatedAt = now,
            UpdatedBy = by,
            Id = 5,
            Label = "Timbal 15\"",
            Attributes = "",
            ParentId = 1,
            IsActive = true,
        };
        static Item BaseTimbales = new Item
        {
            UpdatedAt = now,
            UpdatedBy = by,
            Id = 6,
            Label = "Base Tito Puente",
            Attributes = "",
            ParentId = 1,
            IsActive = true,
        };




        private static void CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            context = new ApplicationContext(options);
            context.Database.EnsureCreated();
        }


        [SetUp]
        public async Task Setup()
        {
            CreateContext();
            context.Items.AddRange(
                SetTimbales,
                Timbal14,
                Aro14,
                Tornillo,
                Timbal15,
                BaseTimbales

            );
            await context.SaveChangesAsync();
        }
        [TearDown]
        public void TearDown()
        {
            context.Dispose();
        }

        [Test]
        [TestCase(6)]
        public async Task GetAll_ReturnsAllActiveEntities(int count)
        {
            var repository = new BaseRepository<Item>(context);

            var result = await repository.GetAll();

            Assert.That(result, Has.Count.EqualTo(count));
        }


        [Test]
        [TestCase(1, "Timbal Tito Puente", "", null,1)]
        [TestCase(2, "Timbal 14\"", "", 1,2)]
        public async Task GetById_ReturnsEntity(int id, string name, string attributes, int? parentId, int typeId)
        {
            var repository = new BaseRepository<Item>(context);

            var result = await repository.GetById(id);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result?.Id, Is.EqualTo(id));
                Assert.That(result?.Label, Is.EqualTo(name));
                Assert.That(result?.Attributes, Is.EqualTo(attributes));
                Assert.That(result?.ParentId, Is.EqualTo(parentId));

            });
        }



        [Test]
        [TestCase(1, null, 3, 1)]
        [TestCase(2, 1, 2, 2)]
        [TestCase(3, 2, 0, 3)]
        [TestCase(4, 2, 0, 3)]
        [TestCase(5, 1, 0, 2)]
        [TestCase(6, 1, 0, 2)]
        public async Task GetById_ReturnsChildEntities(int id, int? parentId, int? childrenNumber, int typeId)
        {
            var repository = new BaseRepository<Item>(context);

            var result = await repository.GetById(id);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result?.Id, Is.EqualTo(id));
                Assert.That(result?.ParentId, Is.EqualTo(parentId));
                Assert.That(result?.Children.Count(), Is.EqualTo(childrenNumber));
            });
        }


    }
}
