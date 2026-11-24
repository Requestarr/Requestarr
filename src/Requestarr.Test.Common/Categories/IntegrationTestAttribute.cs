using NUnit.Framework;

namespace Requestarr.Test.Common.Categories
{
    public class IntegrationTestAttribute : CategoryAttribute
    {
        public IntegrationTestAttribute()
            : base("IntegrationTest")
        {
        }
    }
}
