using System;
using NUnit.Framework;

namespace JsonConfig.Tests
{
	[TestFixture()]
	public class InvalidJson
	{
		[Test]
		[ExpectedException (typeof(JsonFx.Serialization.DeserializationException))]
		public void EvidentlyInvalidJson ()
		{
			dynamic scope = Config.Global.User as ConfigObject;
			scope.ApplyJson ("jibberisch");
		}
		[Test]
		[ExpectedException (typeof(JsonFx.Serialization.DeserializationException))]
		public void MissingObjectIdentifier()
		{
            dynamic scope = Config.Global.User as ConfigObject;
			var invalid_json = @" { [1, 2, 3] }";	
			scope.ApplyJson (invalid_json);
		}
	}
}

