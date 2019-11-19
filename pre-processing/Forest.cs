using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.Diagnostics;

using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.Geodatabase;

namespace pre_processing
{
    class Forest
    {
        private List<Tree> forest; //森林

        //构造函数
        public Forest() { this.forest = new List<Tree>();}

        //析构函数
        ~Forest() { this.forest.Clear();}
        public void Clear() { this.forest.Clear();}
        public int Size() { return this.forest.Count; }
        public List<Tree> Trees() { return this.forest; }
        public Tree GetTree(int index)
        {
            if (index < 0 || index > forest.Count - 1) return null;
            return this.forest[index];
        }

        /// <summary>
        /// 返回除了根节点之外的其它节点id的集合
        /// </summary>
        public List<int> QueryLinkID()
        {
            List<int> linkIDStay = new List<int>(); 
            foreach (Tree tree in this.forest)
            {
                List<int> ids = tree.TraverseWithoutRoot();
                ids.RemoveAt(0); //剔除根节点
                linkIDStay = linkIDStay.Union(ids).ToList<int>();
            }
            return linkIDStay;
        }

        /// <summary>
        /// 添加一棵树
        /// </summary>
        /// <param name="polyID">标签</param>
        /// <param name="featClass">要素类</param>
        /// <param name="uniqueTouch">唯一连接表</param>
        public void AddTree(int polyID, IFeatureClass featClass, Dictionary<int, Posi> uniqueTouch)
        {
            int minTreeAngle = 135; //剪枝角度阈值
            IPolyline polyline = featClass.GetFeature(polyID).ShapeCopy as IPolyline;
            Posi tag = uniqueTouch[polyID];
            Tree tree = new Tree(polyID); //新建一棵树
            tree.InsertAsRoot(polyID, polyline, 0);

            if(tag != Posi.BOTH)
            {
                InitTag(tag, minTreeAngle, ref tree, featClass); //初始化FROM标签
                if (tree.Size() > 1) //tag有更新
                    GoForward(tag, minTreeAngle, ref tree, featClass); //向FROM端延申
            }
            else
            {
                InitTag(Posi.FROM, minTreeAngle, ref tree, featClass); //初始化FROM标签
                if (tree.Size() > 1) //tag有更新
                    GoForward(Posi.FROM, minTreeAngle, ref tree, featClass); //向FROM端延申

                InitTag(Posi.TO, minTreeAngle, ref tree, featClass); //初始化FROM标签
                if (tree.Size() > 1) //tag有更新
                    GoForward(Posi.TO, minTreeAngle, ref tree, featClass); //向FROM端延申
            }

            this.forest.Add(tree);
        }

        /// <summary>
        /// 初始化标签
        /// </summary>
        /// <param name="tag">标签</param>
        /// <param name="minTreeAngle">树剪枝阈值</param>
        /// <param name="tree">树结构</param>
        /// <param name="featClass">要素类</param>
        private void InitTag(Posi tag, double minTreeAngle, ref Tree tree, IFeatureClass featClass)
        {
            //确定tag
            int polyID = tree.Root().id;
            //寻找与polyID相接触的Polyline集合
            IPolyline polyline = featClass.GetFeature(polyID).ShapeCopy as IPolyline;
            IPoint touchPoint = null;
            if (tag == Posi.FROM) touchPoint = polyline.FromPoint;
            else if (tag == Posi.TO) touchPoint = polyline.ToPoint;
            ISpatialFilter sf = new SpatialFilterClass();
            sf.Geometry = touchPoint;
            sf.GeometryField = "SHAPE";
            sf.SpatialRel = esriSpatialRelEnum.esriSpatialRelTouches;
            IFeatureCursor fc = featClass.Search(sf, false);

            int num = featClass.FeatureCount(sf);
            IFeature fea = null;
            while ((fea = fc.NextFeature()) != null)
            {
                int id = fea.OID;
                if (id == polyID) continue; //忽略自身
                if(Convert.ToInt32(fea.get_Value(Program.linkIndex)) != 1) continue; //不是link，跳过
                IPolyline poly = fea.ShapeCopy as IPolyline;
                if (num > 2 && GetPolylineAngle(polyID, id, featClass) < minTreeAngle) continue; //剪枝
                if (GetRel(id, polyID, featClass) == Posi.FROM) //polyID相接于id的FROM端
                    tree.InsertAsChild(tree.Root(), id, poly, poly.Length, tag, Posi.TO); //暴露点exp为TO端
                else if (GetRel(id, polyID, featClass) == Posi.TO) //polyID相接于id的TO端
                    tree.InsertAsChild(tree.Root(), id, poly, poly.Length, tag, Posi.FROM); //暴露点exp为FROM端
            }
        }

        /// <summary>
        /// 向某一个方向延申
        /// </summary>
        /// <param name="tag">延申的方向</param>
        /// <param name="selected">选框元素的OID</param>
        /// <param name="tree">树结构</param>
        /// <param name="featClass">要素类</param>
        private void GoForward(Posi tag, double minTreeAngle, ref Tree tree, IFeatureClass featClass)
        {
            while (true)
            {
                Boolean bUpdate = false; //树是否更新过
                List<Node> leaves = tree.FindLeaves(tag);
                foreach (Node leaf in leaves)
                {
                    //寻找相接触的Polyline
                    //寻找与polyID相接触的Polyline集合
                    IPoint touchPoint = null;
                    if (leaf.exp == Posi.FROM) touchPoint = leaf.poly.FromPoint;
                    else if (leaf.exp == Posi.TO) touchPoint = leaf.poly.ToPoint;
                    ISpatialFilter sf = new SpatialFilterClass();
                    sf.Geometry = touchPoint;
                    sf.GeometryField = "SHAPE";
                    sf.SpatialRel = esriSpatialRelEnum.esriSpatialRelTouches;
                    IFeatureCursor fc = featClass.Search(sf, false);

                    int num = featClass.FeatureCount(sf);
                    IFeature fea = null;
                    while ((fea = fc.NextFeature()) != null)
                    {
                        int id = fea.OID;
                        if (id == leaf.id) continue; //排除自身
                        if(Convert.ToInt32(fea.get_Value(Program.linkIndex)) != 1) continue; //不是link，跳过
                        if (num > 2 && GetPolylineAngle(leaf.id, id, featClass) < minTreeAngle) continue; //剪枝
                        IPolyline poly = fea.ShapeCopy as IPolyline;
                        if (GetRel(id, leaf.id, featClass) == Posi.FROM)
                            tree.InsertAsChild(leaf, id, poly, poly.Length, tag, Posi.TO);
                        else if (GetRel(id, leaf.id, featClass) == Posi.TO)
                            tree.InsertAsChild(leaf, id, poly, poly.Length, tag, Posi.FROM);

                        bUpdate = true;
                    }
                }
                if (!bUpdate) break; //此次无更新，退出
            }
        }

        /// <summary>
        /// 获取Polyline之间的夹角(角度)
        /// </summary>
        /// <param name="id1">Base Polyline的OID</param>
        /// <param name="id2">待比较Polyline的OID</param>
        /// <param name="featureClass">要素类</param>
        private double GetPolylineAngle(int id1, int id2, IFeatureClass featureClass)
        {
            Posi IItoI = GetRel(id1, id2, featureClass);
            Posi ItoII = GetRel(id2, id1, featureClass);
            double[] v1 = null;
            double[] v2 = null;
            ISegmentCollection sc1 = featureClass.GetFeature(id1).ShapeCopy as ISegmentCollection;
            ISegmentCollection sc2 = featureClass.GetFeature(id2).ShapeCopy as ISegmentCollection;

            if (IItoI == Posi.FROM)
            {
                ISegment seg = sc1.get_Segment(0);
                v1 = new double[2] { seg.ToPoint.X - seg.FromPoint.X, seg.ToPoint.Y - seg.FromPoint.Y };
            }
            else
            {
                ISegment seg = sc1.get_Segment(sc1.SegmentCount - 1);
                v1 = new double[2] { seg.FromPoint.X - seg.ToPoint.X, seg.FromPoint.Y - seg.ToPoint.Y };
            }

            if (ItoII == Posi.FROM)
            {
                ISegment seg = sc2.get_Segment(0);
                v2 = new double[2] { seg.ToPoint.X - seg.FromPoint.X, seg.ToPoint.Y - seg.FromPoint.Y };
            }
            else
            {
                ISegment seg = sc2.get_Segment(sc2.SegmentCount - 1);
                v2 = new double[2] { seg.FromPoint.X - seg.ToPoint.X, seg.FromPoint.Y - seg.ToPoint.Y };
            }

            return getVetorAngle(v1, v2);
        }

        /// <summary>
        /// 计算两个向量之间的夹角（角度制）
        /// </summary>
        /// <param name="vector1">一个向量</param>
        /// <param name="vector2">另一个向量</param>
        private double getVetorAngle(double[] vector1, double[] vector2)
        {
            double X1 = vector1[0];
            double Y1 = vector1[1];
            double X2 = vector2[0];
            double Y2 = vector2[1];
            double cos = (X1 * X2 + Y1 * Y2) / (Math.Sqrt(X1 * X1 + Y1 * Y1) * Math.Sqrt(X2 * X2 + Y2 * Y2));
            double radian = Math.Acos(cos);
            double degree = 180 * radian / Math.PI;
            return degree;
        }

        /// <summary>
        /// 获取Polyline之间的位置关系（id2相对于id1的位置）
        /// </summary>
        /// <param name="id1">Base Polyline的OID</param>
        /// <param name="id2">待比较Polyline的OID</param>
        /// <param name="featureClass">要素类</param>
        private Posi GetRel(int id1, int id2, IFeatureClass featureClass)
        {
            IFeature fea1 = featureClass.GetFeature(id1);
            IFeature fea2 = featureClass.GetFeature(id2);
            IPolyline polyline2 = fea2.ShapeCopy as IPolyline;
            IPoint fp1 = (fea1.ShapeCopy as IPolyline).FromPoint;
            IPoint tp1 = (fea1.ShapeCopy as IPolyline).ToPoint;

            if ((polyline2 as IRelationalOperator).Touches(fp1 as IGeometry)) return Posi.FROM;
            else if ((polyline2 as IRelationalOperator).Touches(tp1 as IGeometry)) return Posi.TO;
            else return Posi.NONE;
        }
    }
}
