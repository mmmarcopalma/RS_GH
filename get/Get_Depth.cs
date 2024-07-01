using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Timers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Intel.RealSense;
using Intel.RealSense.Math;
using Rhino;
using Rhino.Geometry;
using Rhino.UI;
using static System.Collections.Specialized.BitVector32;

namespace realsense_gh.Get
{   

    public class rs_Get_Depth : GH_Component
    {

        class DataReceiver

        {
            public rs_Get_Depth parent;
            public DepthFrame df;
            public Pipeline pipeline;
            public Config cfg;
            DeviceList devices;
            Device dev;
                 
            public DataReceiver(rs_Get_Depth parent)
            {
                this.parent = parent;
            }

            public void GetDepthStream(bool on, bool get, int w, int h)
            {

                if (on && get)
                {
                    try
                    {
                        // Create and config the pipeline to stream depth frames.
                        pipeline = new Pipeline();

                        using (var ctx = new Context())
                        {
                            devices = ctx.QueryDevices();
                            dev = devices[0];

                            var sensors = dev.QuerySensors();
                            var depthSensor = sensors[0];

                            var depthProfile = depthSensor.StreamProfiles
                                                .Where(p => p.Stream == Stream.Depth)
                                                .OrderBy(p => p.Framerate)
                                                .Select(p => p.As<VideoStreamProfile>()).First();

                            // read-write advanced sensor options (see Properties > launchSettings.json)
                            foreach (var item in depthSensor.Options)
                            {
                                if (item.Key.ToString() == "MinDistance") { item.Value = 190; }
                            };

                            cfg = new Config();
                            cfg.EnableStream(Stream.Depth, w, h, depthProfile.Format, depthProfile.Framerate);


                            var pp = pipeline.Start(cfg, frame =>

                            {
                                if (frame.IsComposite)
                                {
                                    using (var frames = frame.As<FrameSet>())
                                    {

                                        var depthFrame = frames.FirstOrDefault<DepthFrame>(Stream.Depth);
                                        if (depthFrame != null)
                                        {
                                            df = depthFrame;
                                            //RhinoApp.WriteLine(df.ToString());
                                        }
                                    }
                                }

                            });

                        }

                    }
                    catch (Exception ex)
                    {
                        Rhino.RhinoApp.WriteLine(ex.Message);
                    }
                }

                // camera off ---> reset resolution
                if (!on && !get)
                {
                    if (pipeline != null && cfg != null)
                    {
                        pipeline.Dispose();
                        cfg.DisableAllStreams();
                        cfg.Dispose();
                    }
                }
            }
        }

        // variables
        private DataReceiver receiver;
        private static bool go;
        public string log;
        public int counter = 0;
        public int count = 0;

        // trees
        GH_Structure<GH_PointCloud> cameraPointCloudsTree = new GH_Structure<GH_PointCloud>();

        public rs_Get_Depth()
          : base("rs_Get_Depth", "get_depth",
                 "Gets Depth data from Intel RealSense L515 device as Grasshopper PointCloud.",
                 "RS", "Get")
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {

            pManager.AddBooleanParameter("Power", "P", "Power camera on", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Get", "G", "Get RS Data", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Resolution", "R", "Choose resolution of Depth Sensor: 0:LOW / 1:MID / 2:HIGH", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Max Distance", "M", "Filter points further than this value.", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Min Distance", "m", "Filter points closer than this value.", GH_ParamAccess.item, 0.1);
            pManager.AddIntegerParameter("Frequency", "F", "Frequency of autoupdate in ms", GH_ParamAccess.item, 1000);
            pManager.AddBooleanParameter("Record", "R", "Record RS Data", GH_ParamAccess.item, false);
            
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Log", "L", "Log data from RS", GH_ParamAccess.item);
            pManager.AddGenericParameter("Depth PtCloud", "D", "RS depth point cloud", GH_ParamAccess.item);
            pManager.AddGenericParameter("Depth PtCloud recorded", "C", "Recorded RS depth point clouds", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool get = false;
            int res = 0;
            bool power = false;
            double maxDist = 10.0;
            double minDist = 0.1;
            int frequency = 1000;
            bool record = false;
            
            DA.GetData(0, ref power);
            DA.GetData(1, ref get);
            DA.GetData(2, ref res);
            DA.GetData(3, ref maxDist);
            DA.GetData(4, ref minDist);
            DA.GetData(5, ref frequency);
            DA.GetData(6, ref record);
            
            // vars
            Intel.RealSense.PointCloud pc = new Intel.RealSense.PointCloud();
            List<Point3d> cloudPoints = new List<Point3d>();
            GH_PointCloud gh_cloud = new GH_PointCloud(); ;
            Points pts;
            go = false;
            int width = 320;
            int height = 240;

            if (res == 0) { width = 320; height = 240; }
            if (res == 1) { width = 640; height = 480; }
            if (res == 2) { width = 1024; height = 768; }

            if (power)
            {
                if (count == 0)
                {
                    log = "Camera on. \n";
                    // init receiver class
                    go = true;
                    receiver = new DataReceiver(this);
                    receiver.GetDepthStream(power, go, width, height);
                    count++;
                }

                if (get)
                {
                    DepthFrame depth = receiver.df;

                    if (depth != null)
                    {
                        log = "Camera on. \n";
                        log += depth.ToString() + " " + depth.Width.ToString() + "x" + depth.Height.ToString() + "\n";

                        // process depth frame >>> rh points
                        pts = pc.Process(depth).As<Points>();
                        var vertices = new Vertex[pts.Count];
                        pts.CopyVertices(vertices);

                        for (int i = 0; i < pts.Count; ++i)
                        {
                            double x = Math.Round(vertices[i].x, 5);
                            double y = Math.Round(vertices[i].y, 5);
                            double z = Math.Round(vertices[i].z, 5);

                            Vector3d rv = new Vector3d();
                            rv.X = x;
                            rv.Y = y;
                            rv.Z = z;

                            if (rv.Length < maxDist && rv.Length > minDist)
                            {
                                Point3d rhpt = new Point3d(x * 1000, y * 1000, z * 1000);
                                cloudPoints.Add(rhpt);
                            }
                        }

                        // GH/RH point cloud
                        gh_cloud = new GH_PointCloud(new Rhino.Geometry.PointCloud(cloudPoints));

                        // Handle recorded data
                        if (record)
                        {
                            if (counter < 1000)
                            {
                                // store frames 
                                GH_Path path = new GH_Path(counter);
                                cameraPointCloudsTree.Append(gh_cloud, path);
                            }

                            counter++;
                        }
                    }

                    else
                    {
                        log += "Depth Frame skipped \n";
                    }

                    // Schedule autoupdate
                    OnPingDocument().ScheduleSolution(frequency, doc =>
                    {
                        ExpireSolution(false);
                    });
                }

                else
                {
                    // clear data
                    log += "";
                    cameraPointCloudsTree.Clear();
                    cloudPoints.Clear();
                    counter = 0;
                }
            }

            else
            {
                log = "Camera off. \n";

                if (count > 0)
                {
                    receiver.GetDepthStream(power, go, width, height);
                    go = false;
                    count = 0;
                }
            }

            DA.SetData(0, log);
            DA.SetData(1, gh_cloud);
            DA.SetDataTree(2, cameraPointCloudsTree);

        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.GH_RS_icon_24; 
  

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("EAD59D29-0858-4E9B-BE71-B73E57548091"); }
        }
    }
}