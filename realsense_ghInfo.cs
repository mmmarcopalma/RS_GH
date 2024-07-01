using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace realsense_gh
{
    public class realsense_ghInfo : GH_AssemblyInfo
    {
        public override string Name => "realsense_gh";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "";

        public override Guid Id => new Guid("589f3637-9f4e-4575-98b8-140bd936daa6");

        //Return a string identifying you or your company.
        public override string AuthorName => "";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";
    }
}