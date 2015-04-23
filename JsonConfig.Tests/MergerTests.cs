using System.Dynamic;
using NUnit.Framework;

namespace JsonConfig.Tests
{
    [TestFixture()]
    public class MergerTests : BaseTest
    {
        [Test]
        public void FirstObjectIsNull()
        {
            dynamic x = 1;
            dynamic result = Merger.Merge (null, x);
            Assert.IsInstanceOfType (typeof(int), result);	
            Assert.AreEqual (1, result);
        }
        [Test]
        public void SecondObjectIsNull ()
        {
            dynamic x = 1;
            dynamic result = Merger.Merge (x, null);
            Assert.IsInstanceOfType (typeof(int), result);	
            Assert.AreEqual (1, result);
        }
        [Test]
        public void BothObjectsAreNull ()
        {
            dynamic result = Merger.Merge (null, null);
            Assert.IsInstanceOfType (typeof(ConfigObject), result);
        }
        [Test]
        public void CastToConfigObject ()
        {
            dynamic e = new ExpandoObject ();
            e.Foo = "bar";
            e.X = 1;

            dynamic c = ConfigObject.FromExpando (e);

            Assert.IsInstanceOfType (typeof(ConfigObject), c);
            Assert.AreEqual ("bar", c.Foo);
            Assert.AreEqual (1, c.X);
        }
        [Test]
        [ExpectedException(typeof(TypeMissmatchException))]
        public void TypesAreDifferent ()
        {
            dynamic x = "somestring";
            dynamic y = 1;
            dynamic result = Merger.Merge (x, y);
            // avoid result is assigned but never used warning
            Assert.AreEqual (0, result);
        }
        /// <summary>
        /// If one of the objects is a NullExceptionPreventer, the other object is returned unchanged but 
        /// as a ConfigObject
        /// </summary>
        [Test]
        public void MergeEmptyConfigObject()
        {
            var n = new ConfigObject();
            var c = Config.ApplyJson (@"{ ""Sample"": ""Foobar"" }", new ConfigObject ());

            // merge left
            dynamic merged = Merger.Merge (c, n);
            Assert.IsInstanceOfType (typeof(ConfigObject), merged);
            Assert.That (merged.Sample == "Foobar");

            // merge right
            merged = Merger.Merge (n, c);
            Assert.IsInstanceOfType (typeof(ConfigObject), merged);
            Assert.That (merged.Sample == "Foobar");
        }
        [Test]
        public void MergeTwoEmptyConfigObject ()
        {
            var n1 = new ConfigObject ();
            var n2 = new ConfigObject ();
            dynamic merged = Merger.Merge (n1, n2);
            Assert.IsInstanceOfType (typeof(ConfigObject), merged);
        }
        [Test]
        public void MergeEmptyExpandoObject ()
        {
            // Merge a ExpandoObject with an empty Expando
            // should return a ConfigObject
            dynamic e = new ExpandoObject ();
            e.Foo = "Bar";
            e.X = 1;
            dynamic merged = Merger.Merge (e, new ExpandoObject ());
            Assert.IsInstanceOfType (typeof(ConfigObject), merged);

            Assert.IsInstanceOfType (typeof(int), merged.X);
            Assert.IsInstanceOfType (typeof(string), merged.Foo);

            Assert.AreEqual ("Bar", merged.Foo);
            Assert.AreEqual (1, merged.X);
        }
        [Test]
        public void MergeConfigObjects ()
        {
            dynamic c1 = new ConfigObject ();
            dynamic c2 = new ConfigObject ();
            c1.Foo = "bar";
            c2.Bla = "blubb";
            dynamic merged = Merger.Merge (c1, c2);
            Assert.IsInstanceOfType (typeof(ConfigObject), merged);
            Assert.AreEqual ("bar", merged.Foo);
            Assert.AreEqual ("blubb", merged.Bla);
        }
        [Test]
        public void MergeEmptyConfigObjects ()
        {
            dynamic c1 = new ConfigObject ();
            dynamic c2 = new ConfigObject ();

            c1.Foo = "bar";
            c1.X = 1;
            dynamic merged = Merger.Merge (c1, c2);

            Assert.IsInstanceOfType (typeof(ConfigObject), merged);
            Assert.AreEqual ("bar", c1.Foo);
            Assert.AreEqual (1, c1.X);
        }
        [Test]
        public void MaintainHierarchy ()
        {
            dynamic @default = new ConfigObject ();
            dynamic user = new ConfigObject ();
            dynamic scope = new ConfigObject ();

            @default.Foo = 1;
            user.Foo = 2;
            scope.Foo = 3;

            dynamic merged = Merger.MergeMultiple (scope, user, @default);
            Assert.AreEqual (3, merged.Foo);

        }

    }
}

