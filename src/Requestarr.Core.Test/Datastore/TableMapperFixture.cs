using System.Collections.Generic;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Converters;

namespace Requestarr.Core.Test.Datastore
{
    [TestFixture]
    public class TableMapperFixture
    {
        public class EmbeddedType : IEmbeddedDocument
        {
        }

        public class TypeWithAllMappableProperties
        {
            public string PropString { get; set; }
            public int PropInt { get; set; }
            public bool PropBool { get; set; }
            public int? PropNullable { get; set; }
            public EmbeddedType Embedded { get; set; }
            public List<EmbeddedType> EmbeddedList { get; set; }
        }

        public class TypeWithNoMappableProperties
        {
#pragma warning disable S1144 // The private accessors are the point: the mapper must skip a property without a public getter and setter
            public int ReadOnly { get; private set; }
            public int WriteOnly { private get; set; }
#pragma warning restore S1144 // Only the two properties above
        }

        [SetUp]
        public void Setup()
        {
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<EmbeddedType>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<EmbeddedType>());
        }

        [Test]
        public void test_mappable_types()
        {
            var properties = typeof(TypeWithAllMappableProperties).GetProperties();
            properties.Should().NotBeEmpty();
            properties.Should().OnlyContain(c => c.IsMappableProperty());
        }

        [Test]
        public void test_un_mappable_types()
        {
            var properties = typeof(TypeWithNoMappableProperties).GetProperties();
            properties.Should().NotBeEmpty();
            properties.Should().NotContain(c => c.IsMappableProperty());
        }
    }
}
