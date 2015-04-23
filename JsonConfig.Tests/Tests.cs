using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using JsonFx.Json;
using NUnit.Framework;

namespace JsonConfig.Tests
{
    [TestFixture]
    public class BunchOfTests : BaseTest
    {
        [Test]
        public void Arrays()
        {
            dynamic parsed = GetUUT("Arrays");
            dynamic merged = Merger.Merge(parsed.Fruit1, parsed.Fruit2);

            var fruitList = merged.Fruit as ICollection<string>;
            // ReSharper disable PossibleNullReferenceException
            Assert.AreEqual(6, fruitList.Count);
            // ReSharper restore PossibleNullReferenceException
            // apple must be in it 2 times, since array merging is NOT SET merging!
            Assert.AreEqual(fruitList.Count(f => f == "apple"), 2);
            Assert.That(fruitList.Contains("coconut"));
        }

        [Test]
        public void ArrayWithEmptyArray()
        {
            dynamic parsed = GetUUT("Arrays");
            dynamic merged = Merger.Merge(parsed.Fruit1, parsed.EmptyFruit);

            var fruitList = merged.Fruit as ICollection<string>;
            // ReSharper disable PossibleNullReferenceException
            Assert.AreEqual(3, fruitList.Count);
            // ReSharper restore PossibleNullReferenceException
            Assert.That(fruitList.Contains("apple"));
            Assert.That(fruitList.Contains("banana"));
            Assert.That(fruitList.Contains("melon"));
        }

        [Test]
        public void CanAccessNonExistantField()
        {
            dynamic parsed = GetUUT("Arrays");
            dynamic merged = Merger.Merge(parsed.Fruit1, parsed.Fruit2);

            Assert.That(string.IsNullOrEmpty(merged.field.not.exist.ToString()));
            Assert.That(string.IsNullOrEmpty(merged.thisfield.does.just.not.exist));
        }

        [Test]
        public void ComplexArrayWithEmptyArray()
        {
            dynamic parsed = GetUUT("Arrays");
            dynamic merged = Merger.Merge(parsed.Coords1, parsed.Coords2);

            Assert.AreEqual(2, merged.Pairs.Length);
        }

        //[Test]
        //public void DefaultConfigFound ()
        //{
        //    Assert.IsNotNull (Config.Global.User);
        //    Assert.That (Config.Default.Sample == "found");
        //}
        [Test]
        public void ComplexTypeWithArray()
        {
            dynamic parsed = GetUUT("Foods");
            dynamic fruit = parsed.Fruits;
            dynamic vegetables = parsed.Vegetables;

            dynamic result = Merger.Merge(fruit, vegetables);

            Assert.AreEqual(6, result.Types.Length);
            Assert.IsInstanceOfType(typeof (ConfigObject), result);
            Assert.IsInstanceOfType(typeof (ConfigObject[]), result.Types);
        }

        [Test]
        public void CurrentScopeTest()
        {
            dynamic c = Config.Global.GetCurrentScope();
            c.ApplyJson(@"{ Foo : 1, Bar: ""blubb"" }");
            Assert.AreEqual(1, c.Foo);
            Assert.AreEqual("blubb", c.Bar);
        }

        //[Test]
        //public void ManualDefaultAndUserConfig ()
        //{
        //    dynamic parsed = GetUUT ("Foods");

        //    Config.Global.SetUserConfig (parsed.Fruits);
        //    Config.Global.SetDefaultConfig(parsed.Vegetables);

        //    Assert.IsInstanceOfType (typeof(ConfigObject), Config.Global.User);
        //    //Assert.IsInstanceOfType(typeof(ConfigObject), Config.Global.Default);

        //    dynamic scope = Config.Global.User;
        //    scope = Config.ApplyJson (@"{ Types : [{Type : ""Salad"", PricePerTen : 5 }]}", scope);
        //    Assert.AreEqual (7, scope.Types.Length);
        //}
        [Test]
        public void EnabledModulesTest()
        {
            // classical module scenario: user specifies what modules are to be loaded

            dynamic modules = GetUUT("EnabledModules");

            // method one : use an object with each module name as key, and value true/false
            dynamic modulesObject = modules.EnabledModulesObject;
            Assert.AreNotEqual(null, modulesObject.Module1);
            Assert.AreNotEqual(null, modulesObject.Module2);

            Assert.That(modulesObject.Module1 == true);
            Assert.That(!modulesObject.Module1 == false);

            Assert.That(modulesObject.Module2 == false);

            // tricky part: NonExistantModule is not defined in the json but should be false anyways
            Assert.That(modulesObject.NonExistantModule == false);
            Assert.That(!modulesObject.NonExistantModule == true);
            Assert.That(modulesObject.NonExistantModule.Nested.Field.That.Doesnt.Exist == false);
        }

        [Test]
        public void FirewallConfig()
        {
            dynamic parsed = GetUUT("Firewall");
            dynamic merged = Merger.Merge(parsed.UserConfig, parsed.FactoryDefault);

            var interfaces = merged.Interfaces as ICollection<string>;
            // ReSharper disable AssignNullToNotNullAttribute
            Assert.AreEqual(3, interfaces.Count());
            // ReSharper restore AssignNullToNotNullAttribute

            var zones = merged.Zones as ICollection<dynamic>;

            // ReSharper disable AssignNullToNotNullAttribute
            var loopback = zones.Count(d => d.Name == "Loopback");
            // ReSharper restore AssignNullToNotNullAttribute
            Assert.AreEqual(1, loopback);

            // one portmapping is present at least
            // ReSharper disable AssignNullToNotNullAttribute
            var intzone = zones.First(d => d.Name == "Internal");
            // ReSharper restore AssignNullToNotNullAttribute
            Assert.That(intzone.PortMapping != null);
            Assert.Greater(intzone.PortMapping.Length, 0);
        }

        [Test]
        public void Product()
        {
            dynamic parsed = GetUUT("Product");
            dynamic merged = Merger.Merge(parsed.Amazon, parsed.WalMart);

            Assert.That(merged.Price == 129);
            Assert.That(merged.Rating.Comments.Length == 3);

            // only float values should be in the rating
            var stars = merged.Rating.Stars as ICollection<double>;
            Assert.IsNotNull(stars);

            // ReSharper disable CompareOfFloatsByEqualityOperator
            Assert.That(stars.Sum(d => d) == 12.5);
            // ReSharper restore CompareOfFloatsByEqualityOperator
        }

        [Test]
        public void SaveTest()
        {
            var userConfigFileName = Assembly.GetExecutingAssembly().GetName().Name + Config.DefaultEnding;
            if (File.Exists(userConfigFileName))
            {
                File.Delete(userConfigFileName);
            }
            Config.Local.User.Name = "yqy";
            Config.Local.Save();
            dynamic result;
            using (var sr = new StreamReader(userConfigFileName))
            {
                JsonReader reader = new JsonReader();
                result = reader.Read(sr);
                Assert.AreEqual(result.Name, "yqy");
            }
            result.Age = 20;
            using (var sw = new StreamWriter(userConfigFileName))
            {
                JsonWriter writer = new JsonWriter();
                writer.Settings.PrettyPrint = true;
                Config.Local.SuspendWatchUserConfig();
                writer.Write(result, sw);
                Config.Local.ResumeWatchUserConfig();
            }
            Thread.Sleep(1000);
            Assert.IsInstanceOf<int>(Config.Local.User.Age);
            Config.Local.User.Dragon.Long = 20;
            Assert.AreEqual(Config.Local.User.Age, 20);
        }
    }
}