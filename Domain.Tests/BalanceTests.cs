using Domain.Entities;

namespace Domain.Tests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            var balance = new Balance("id", 42, 42);
        }
    }
}
