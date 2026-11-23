using NUnit.Framework;

namespace Requestarr.Test.Common.Categories
{
    public class ManualTestAttribute : CategoryAttribute
    {
        public ManualTestAttribute()
            : base("ManualTest")
        {
        }
    }
}
