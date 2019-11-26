using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading;
using System.Diagnostics;

using ESRI.ArcGIS;
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.DataSourcesFile;
using ESRI.ArcGIS.DataSourcesGDB;
using ESRI.ArcGIS.Framework;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Geoprocessing;
using ESRI.ArcGIS.Geometry;

namespace pre_processing
{
    public enum Posi { NONE, FROM, TO, BOTH};
    public enum Strategy { Tree, Stroke};
    class Program
    {
        static AoInitialize m_AoInitialize;

        //定义全局变量
        //应该保留下来的道路类型
        static public List<string> roadTypeShouldStay = new List<string>()
        {"motorway", "motorway_link", "trunk", "trunk_link", "primary", "primary_link", "secondary", "secondary_link" };
        static public List<string> linkRoadType = new List<string>() { "motorway_link", "trunk_link", "primary_link", "secondary_link" };
        static public int roadTypeIndex = -1; //字段“highway”在属性表中的索引
        static public int linkIndex = -1;

        static void Main(string[] args)
        {
            ESRI.ArcGIS.RuntimeManager.Bind(ESRI.ArcGIS.ProductCode.EngineOrDesktop);
            AoInitializeFirst();

            string inPath = "E:\\桌面\\道路平行性判断\\CityShapeFile\\Hongkong\\highway.shp";
            RemoveRoadByType(inPath);
            RemoveLink(inPath, Strategy.Tree);
            RemoveLink(inPath, Strategy.Stroke);
            //DeleteLinkLayer(inPath);
        }

        /// <summary>
        /// 按类型删除线段
        /// <summary>
        /// <param name="path">shp文件路径</param>
        static private void RemoveRoadByType(string inPath)
        {
            //计时
            Stopwatch sw = new Stopwatch();
            sw.Start();

            //string inPath = "E:\\桌面\\道路平行性判断\\CityShapeFile\\#test\\hk_island_spf.shp";
            string dir = System.IO.Path.GetDirectoryName(inPath); //可以获得不带文件名的路径
            string name = System.IO.Path.GetFileNameWithoutExtension(inPath); //可以获得文件名
            string linkPath = dir + "\\" + name + "_link.shp";


            Console.WriteLine("inPath: " + inPath);
            Console.WriteLine("linkPath: " + linkPath);

            IFeatureClass linkFeatClass = CopyFeatureClass(inPath, linkPath); //拷贝要素类

            roadTypeIndex = QueryFieldIndex(linkFeatClass, "highway");
            linkIndex = AddField(linkFeatClass, "link", esriFieldType.esriFieldTypeSmallInteger, 1);

            //打开编辑器
            IWorkspaceFactory workspaceFactory = new ShapefileWorkspaceFactoryClass();
            IWorkspace workspace = workspaceFactory.OpenFromFile(System.IO.Path.GetDirectoryName(linkPath), 0);
            IWorkspaceEdit workspaceEdit = workspace as IWorkspaceEdit;
            workspaceEdit.StartEditing(true);
            workspaceEdit.StartEditOperation();

            //按类型删除
            DeleteFeatureByRoadType(linkFeatClass);
            //为link做标记
            UpdateTagField(linkFeatClass, linkIndex);

            //停止编辑状态
            workspaceEdit.StopEditOperation();
            workspaceEdit.StopEditing(true);

            //停止计时，输出结果
            sw.Stop();
            int featCount = linkFeatClass.FeatureCount(null);
            Console.WriteLine("规模：" + linkFeatClass.FeatureCount(null) + "  用时：" + Math.Round(sw.Elapsed.TotalMinutes, 2) + "Min");
        }

        /// <summary>
        /// 数据预处理程序（按类型删除->添加link字段->删除link）
        /// <summary>
        /// <param name="path">shp文件路径</param>
        /// <param name="strategy">策略（Tree或Stroke）</param>
        static private void RemoveLink(string inPath, Strategy strategy)
        {
            //计时
            Stopwatch sw = new Stopwatch();
            sw.Start();

            //string inPath = "E:\\桌面\\道路平行性判断\\CityShapeFile\\#test\\hk_island_spf.shp";
            string dir = System.IO.Path.GetDirectoryName(inPath); //可以获得不带文件名的路径
            string name = System.IO.Path.GetFileNameWithoutExtension(inPath); //可以获得文件名
            string linkPath = dir + "\\" + name + "_link.shp";
            string spfPath = null;
            if (strategy == Strategy.Tree)
                spfPath = dir + "\\" + name + "_spf(Tree).shp";
            else if(strategy == Strategy.Stroke)
                spfPath = dir + "\\" + name + "_spf(Stroke).shp";

            Console.WriteLine("linkPath: " + inPath);
            Console.WriteLine("spfPath: " + spfPath);

            IFeatureClass spfFeatClass = CopyFeatureClass(linkPath, spfPath); //拷贝要素类

            roadTypeIndex = QueryFieldIndex(spfFeatClass, "highway");
            linkIndex = AddField(spfFeatClass, "link", esriFieldType.esriFieldTypeSmallInteger, 1);

            //打开编辑器
            IWorkspaceFactory workspaceFactory = new ShapefileWorkspaceFactoryClass();
            IWorkspace workspace = workspaceFactory.OpenFromFile(System.IO.Path.GetDirectoryName(inPath), 0);
            IWorkspaceEdit workspaceEdit = workspace as IWorkspaceEdit;
            workspaceEdit.StartEditing(true);
            workspaceEdit.StartEditOperation();

            //删除link道路
            if (strategy == Strategy.Tree)
                RemoveLinkRoad_Tree(spfFeatClass);
            else if(strategy == Strategy.Stroke)
                RemoveLinkRoad_Stroke(spfFeatClass);

            //停止编辑状态
            workspaceEdit.StopEditOperation();
            workspaceEdit.StopEditing(true);

            //停止计时，输出结果
            sw.Stop();
            int featCount = spfFeatClass.FeatureCount(null);
            Console.WriteLine("规模：" + spfFeatClass.FeatureCount(null) + "  用时：" + Math.Round(sw.Elapsed.TotalMinutes, 2) + "Min");
        }

        /// <summary>
        /// 删除_link图层
        /// <summary>
        /// <param name="path">shp文件路径</param>
        static private void DeleteLinkLayer(string inPath)
        {
            string dir = System.IO.Path.GetDirectoryName(inPath); //可以获得不带文件名的路径
            string name = System.IO.Path.GetFileNameWithoutExtension(inPath); //可以获得文件名
            string linkPath = dir + "\\" + name + "_link.shp";

            if (File.Exists(linkPath)) //存在该路径，则在文件夹中找到文件并删除
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(linkPath); //文件名称
                DirectoryInfo root = new DirectoryInfo(dir);
                foreach (FileInfo f in root.GetFiles())
                {
                    if (f.Name.Split('.')[0] == fileName)
                    {
                        string filepath = f.FullName;
                        File.Delete(filepath);
                    }
                }
            }
        }

        /// <summary>
        /// 按实际情况去除连接道路
        /// <summary>
        /// <param name="featClass">要素类</param>
        static private void RemoveLinkRoad_Tree(IFeatureClass featClass)
        {
            string linkWhereClause = "link = 1"; //筛选条件（筛选出所有link)
            IQueryFilter queryFilter = new QueryFilterClass();
            queryFilter.WhereClause = linkWhereClause;
            IFeatureCursor searchCursor = featClass.Search(queryFilter, false);
            Dictionary<int, Posi> uniqueTouch = new Dictionary<int,Posi>(); //与link唯一连接的要素ID集合
            int featCount = featClass.FeatureCount(queryFilter);

            //筛选出所有跟link唯一连接的主要道路（即在link的端点处仅存在唯一的main道路）
            int count = 0; //计数器
            IFeature linkFeatrue = null;
            while ((linkFeatrue = searchCursor.NextFeature()) != null)
            {
                Console.WriteLine("探测唯一接触点：" + (++count) + " / " + featCount);
                UpdateUniqueTouch(linkFeatrue, Posi.FROM, ref uniqueTouch, featClass); //FROM端更新
                UpdateUniqueTouch(linkFeatrue, Posi.TO, ref uniqueTouch, featClass); //TO端更新
            }
            Marshal.FinalReleaseComObject(searchCursor);

            //foreach (KeyValuePair<int, Posi> kvp in uniqueTouch)
            //    Console.WriteLine("id: " + kvp.Key + "  posi: " + kvp.Value);

            //唯一邻接表的所有线段为根节点生成所有的树
            Forest forest = new Forest(); //空森林
            count = 0;
            foreach (KeyValuePair<int, Posi> kvp in uniqueTouch)
            {
                Console.WriteLine("生成树：" + (++count) + " / " + uniqueTouch.Count);
                int polyID = kvp.Key; Posi posi = kvp.Value;
                forest.AddTree(polyID, featClass, uniqueTouch); //往森林里添加树
            }

            //获取应该保留的边
            List<int> linkIDStay = forest.QueryLinkID();

            //Console.WriteLine("linkIDStay: ");
            //foreach (int id in linkIDStay)
            //    Console.Write(id + " ");

            //删除link
            count = 0;
            IFeatureCursor searchCursor2 = featClass.Search(queryFilter, false); //迭代器
            linkFeatrue = null;
            while ((linkFeatrue = searchCursor2.NextFeature()) != null)
            {
                Console.WriteLine("删除边：" + (++count) + " / " + featCount);
                if (!linkIDStay.Contains(linkFeatrue.OID)) //如果线段不在应该保留的范围之内，则
                    linkFeatrue.Delete(); //删除线段
            }
            Marshal.FinalReleaseComObject(searchCursor2);

            //释放内存
            linkIDStay.Clear();
            forest.Clear();
            uniqueTouch.Clear();
        }

        /// <summary>
        /// 更新唯一邻接表
        /// <summary>
        /// <param name="feature">更新要素</param>
        /// <param name="posi">更新方向</param>
        /// <param name="uniqueTouch">唯一邻接表</param>
        /// <param name="featClass">要素类</param>
        static public void UpdateUniqueTouch(IFeature feature, Posi posi, ref Dictionary<int, Posi> uniqueTouch, IFeatureClass featClass)
        {
            IPolyline polyline = feature.ShapeCopy as IPolyline;
            ISpatialFilter filter = new SpatialFilterClass();
            if (posi == Posi.FROM) //在FROM端更新
                filter.Geometry = polyline.FromPoint;
            if (posi == Posi.TO) //在TO端更新
                filter.Geometry = polyline.ToPoint;
            filter.SpatialRel = esriSpatialRelEnum.esriSpatialRelTouches;
            IFeatureCursor searchCursor = featClass.Search(filter, false);

            int mainOID = -1; //main道路要素OID
            IFeature fea = null;
            while ((fea = searchCursor.NextFeature()) != null) //遍历触点的邻接要素
            {
                if ((fea.ShapeCopy as IPolyline).IsClosed) continue; //跳过环
                if (!IsLink(fea, featClass)) //是主要道路
                    if (mainOID == -1) mainOID = fea.OID; //更新
                    else { mainOID = -1; break; }//main道路OID已经更新过，即存在两段以上的main道路，将mainOID退回为-1
            }
            Marshal.FinalReleaseComObject(searchCursor);

            //更新唯一邻接表
            if (mainOID != -1)
            {
                Posi rel = GetRel(mainOID, feature.OID, featClass); //link道路在main道路的哪一端？
                if (uniqueTouch.ContainsKey(mainOID)) //唯一邻接表已存在该main道路
                {
                    if (uniqueTouch[mainOID] != rel) //考虑将其更新为BOTH
                        uniqueTouch[mainOID] = Posi.BOTH;
                }
                else uniqueTouch.Add(mainOID, rel); //添加唯一邻接段
            }
        }

        /// <summary>
        /// 按实际情况去除连接道路
        /// <summary>
        /// <param name="featClass">要素类</param>
        static private void RemoveLinkRoad_Stroke(IFeatureClass featClass)
        {
            string linkWhereClause = "link = 1";
            IQueryFilter queryFilter = new QueryFilterClass();
            queryFilter.WhereClause = linkWhereClause;
            Console.WriteLine("linkWhereClause: " + linkWhereClause);
            IFeatureCursor searchCursor = featClass.Search(queryFilter, false);
            int featCout = featClass.FeatureCount(queryFilter);
            List<int> visited = new List<int>(); //已访问的要素

            IFeature linkFeatrue = null;
            while ((linkFeatrue = searchCursor.NextFeature()) != null)
            {
                if (visited.Contains(linkFeatrue.OID)) continue;
                Console.WriteLine("删除连接道路：" + visited.Count + "/" + featCout);
                //左右延伸寻找stroke，并删除该stroke中的所有要素
                DeleteFeatureByStrokeTouching(linkFeatrue, featClass, ref visited);
            }
            Marshal.FinalReleaseComObject(searchCursor);
            visited.Clear();
        }

        /// <summary>
        /// 获得从某一要素开始延伸的所有要素
        /// <summary>
        /// <param name="feature">要素</param>
        /// <param name="featClass">要素类</param>
        static private void DeleteFeatureByStrokeTouching(IFeature feature, IFeatureClass featClass, ref List<int> visited)
        {
            //Console.WriteLine("feature id：" + feature.get_Value(id_index).ToString());
            List<int> strokeFeature = new List<int>() { feature.OID }; //存储一段stroke的要素
            //List<int> fea_id = new List<int>() { Convert.ToInt32(feature.get_Value(id_index))}; //存储一段stroke的要素

            //状态码：0（不接触），1(延申到_link)，2（延申到唯一的_main），3（其它）
            int fsc = 1;
            int tsc = 1;

            //沿着feature的polyline前后寻找link道路，将其存储在strokeFeature中，这些道路组合成stroke，
            //如果stroke的两端存在与非link道路的接触，则保留，否则删除
            Posi currExp = Posi.FROM; //延申端
            while (true)
            {
                if (fsc != 1) break; //两边都无法通行，则退出

                int bestPolyID = -1; Posi nextExp = Posi.NONE;
                fsc = EveryBestFit(strokeFeature[0], currExp, featClass, visited, out bestPolyID, out nextExp); //寻找from端的最佳延申延申段
                if (fsc == 1)
                {
                    strokeFeature.Insert(0, bestPolyID);
                    visited.Add(bestPolyID);
                    currExp = nextExp;
                    //fea_id.Insert(0, Convert.ToInt32(bestFeature.get_Value(id_index)));
                }
            }

            currExp = Posi.TO; //延申端
            while (true)
            {
                if (tsc != 1) break; //两边都无法通行，则退出

                int bestPolyID = -1; Posi nextExp = Posi.NONE;
                tsc = EveryBestFit(strokeFeature[strokeFeature.Count - 1], currExp, featClass, visited, out bestPolyID, out nextExp); //寻找from端的最佳延申延申段
                if (tsc == 1) //延申下去
                {
                    strokeFeature.Add(bestPolyID);
                    visited.Add(bestPolyID);
                    currExp = nextExp;
                    //fea_id.Insert(0, Convert.ToInt32(bestFeature.get_Value(id_index)));
                }
            }

            //输出strokeFeature
            //Console.WriteLine("fea_id：");
            //foreach(int id in fea_id)
            //{
            //    Console.Write(id + " ");
            //}
            //Console.WriteLine();

            //删除stroke的条件：任何一端是友好连接的都保留
            if ((fsc == 0 || tsc == 0) || (fsc != 2 && tsc != 2))
            {
                foreach (int oid in strokeFeature) //删除stroke的构成元素
                    featClass.GetFeature(oid).Delete();
            }

            strokeFeature.Clear();
        }

        /// <summary>
        /// 打开要素类
        /// </summary>
        /// <param name="path">shp文件的存储路径</param>
        public static IFeatureClass OpenFeatClass(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            IWorkspace workspace;
            IFeatureWorkspace featureWorkspace;
            IFeatureClass featureClass;

            IWorkspaceFactory workspaceFactory = new ShapefileWorkspaceFactoryClass();
            workspace = workspaceFactory.OpenFromFile(System.IO.Path.GetDirectoryName(path), 0);
            featureWorkspace = workspace as IFeatureWorkspace;
            string filename = System.IO.Path.GetFileNameWithoutExtension(path);

            featureClass = featureWorkspace.OpenFeatureClass(filename);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(featureWorkspace);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(workspaceFactory);
            if (featureClass != null)
            {
                return featureClass;
            }
            return null;
        }

        /// <summary>
        /// 添加字段
        /// </summary>
        /// <param name="featureClass">要素类</param>
        /// <param name="fieldName">字段名</param>
        /// <param name="filedType">字段类型</param>
        static private int AddField(IFeatureClass featureClass, string fieldName, esriFieldType filedType, int precisioin)
        {
            int fieldIndex = featureClass.Fields.FindField(fieldName);
            //判断字段是否存在，若存在则不进行任何操作
            if (fieldIndex > -1) return fieldIndex;

            //添加字段
            IClass pClass = featureClass as IClass;
            IFieldsEdit fieldsEdit = featureClass.Fields as IFieldsEdit;
            IField field = new FieldClass();
            IFieldEdit fieldEdit = field as IFieldEdit;
            fieldEdit.Name_2 = fieldName;
            fieldEdit.Type_2 = filedType;
            if (filedType == esriFieldType.esriFieldTypeSmallInteger)
            {
                fieldEdit.Precision_2 = precisioin;
                fieldEdit.Scale_2 = 0;
                fieldEdit.DefaultValue_2 = 0;
            }
            if (filedType == esriFieldType.esriFieldTypeString)
            {
                fieldEdit.Precision_2 = precisioin;
            }
            pClass.AddField(field);
            return featureClass.Fields.FindField(fieldName);
        }

        /// <summary>
        /// 更新标签字段
        /// </summary>
        /// <param name="featureClass">要素类</param>
        /// <param name="fieldIndex">字段索引</param>
        static private void UpdateTagField(IFeatureClass featClass, int fieldIndex)
        {
            string linkWhereClause = GetWhereClause(linkRoadType);
            IQueryFilter queryFilter = new QueryFilterClass();
            queryFilter.WhereClause = linkWhereClause;

            Console.WriteLine("为link做标记：linkWhereClause = " + linkWhereClause);
            IFeatureCursor searchCursor = featClass.Search(queryFilter, false);
            int featNum = featClass.FeatureCount(queryFilter);
            int count = 0;

            IFeature linkFeatrue = null;
            while ((linkFeatrue = searchCursor.NextFeature()) != null)
            {
                Console.WriteLine("为link做标记：" + (count++) + " / " + featNum);
                linkFeatrue.set_Value(fieldIndex, 1);
                linkFeatrue.Store();
            }
            Marshal.FinalReleaseComObject(searchCursor);
        }

        /// <summary>
        /// 拷贝要素类
        /// </summary>
        /// <param name="sourcePath">需要拷贝的要素类路径</param>
        /// <param name="targetPath">拷贝要素类的路径</param>
        static private IFeatureClass CopyFeatureClass(string sourcePath, string targetPath)
        {
            Console.WriteLine("拷贝要素类");
            IWorkspace workspace;
            IFeatureWorkspace featureWorkspace;
            IWorkspaceFactory workspaceFactory = new ShapefileWorkspaceFactoryClass();
            string path_dir = System.IO.Path.GetDirectoryName(targetPath);
            workspace = workspaceFactory.OpenFromFile(System.IO.Path.GetDirectoryName(targetPath), 0);
            featureWorkspace = workspace as IFeatureWorkspace;

            if (File.Exists(targetPath)) //存在该路径，则在文件夹中找到文件并删除
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(targetPath); //文件名称
                DirectoryInfo root = new DirectoryInfo(path_dir);
                foreach (FileInfo f in root.GetFiles())
                {
                    if (f.Name.Split('.')[0] == fileName)
                    {
                        string filepath = f.FullName;
                        File.Delete(filepath);
                    }
                }
            }

            //拷贝要素类
            IGeoProcessor2 gp = new GeoProcessorClass();
            gp.OverwriteOutput = true;
            IGeoProcessorResult result = new GeoProcessorResultClass();
            IVariantArray parameters = new VarArrayClass();
            object sev = null;
            try
            {
                parameters.Add(sourcePath);
                parameters.Add(targetPath);
                result = gp.Execute("Copy_management", parameters, null);
                Console.WriteLine(gp.GetMessages(ref sev));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(gp.GetMessages(ref sev));
            }

            string name = System.IO.Path.GetFileName(targetPath);
            IFeatureClass featureClass = featureWorkspace.OpenFeatureClass(name);
            return featureClass;
        }

        /// <summary>
        /// 获得条件查询语句
        /// <summary>
        /// <param name="stringSet"></param>
        static private string GetWhereClause(List<string> stringSet)
        {
            if (stringSet.Count == 0)
                return null;

            //条件查询出需要探测尖角的图斑
            string whereClause = "";
            for (int i = 0; i < stringSet.Count(); i++)
            {
                string value = stringSet[i];
                if (i == 0)
                    whereClause = whereClause + "highway = " + "\'" + value + "\'";
                else
                    whereClause = whereClause + "OR highway = " + "\'" + value + "\'";
            }
            return whereClause;
        }

        /// <summary>
        /// 返回指定名称的字段索引
        /// <summary>
        /// <param name="featClass">要素类</param>
        /// <param name="fieldName">字段名称</param>
        static private int QueryFieldIndex(IFeatureClass featClass, string fieldName)
        {
            int fieldIndex = -1;
            //确定"LC_1"和"LC_1_CC"字段的索引值
            for (int i = 0; i < featClass.Fields.FieldCount; i++)
            {
                if (featClass.Fields.get_Field(i).Name == fieldName)
                {
                    fieldIndex = i;
                    break;
                }
            }
            return fieldIndex;
        }

        /// <summary>
        /// 删除特定类型的道路
        /// <summary>
        /// <param name="featClass">要素类</param>
        static private void DeleteFeatureByRoadType(IFeatureClass featClass)
        {
            IFeatureCursor searchCursor = featClass.Search(null, false);
            int featCout = featClass.FeatureCount(null);
            IFeature feature = null;
            while ((feature = searchCursor.NextFeature()) != null)
            {
                Console.WriteLine("按类型删除：" + feature.OID + " / " + featCout);
                string roadType = feature.get_Value(roadTypeIndex).ToString();
                if (!roadTypeShouldStay.Contains(roadType))
                    feature.Delete();
            }
            Marshal.FinalReleaseComObject(searchCursor);
        }

        /// <summary>
        /// 从一个段IPolyline找到最匹配的IPolyline（EveryBestFit延申策略）
        /// </summary>
        /// <param name="polyID">待延申的polyID</param>
        /// <param name="currExp">polyID的暴露端</param>
        /// <param name="featureClass">要素类</param>
        /// <param name="visited">已访问的要素OID</param>
        /// <param name="bestPolyID">下一段Polyline的OID</param>
        /// <param name="nextExp">下一个暴露端</param>
        static private int EveryBestFit(int polyID, Posi currExp, IFeatureClass featureClass, List<int> visited, out int bestPolyID, out Posi nextExp)
        {
            bestPolyID = -1; nextExp = Posi.NONE;
            int mainCount = 0; //表示该接触点有主要道路
            IFeature feature = featureClass.GetFeature(polyID);
            //临边查询
            ISpatialFilter sf = new SpatialFilterClass();
            sf.GeometryField = "SHAPE";
            sf.SpatialRel = esriSpatialRelEnum.esriSpatialRelTouches;
            if (currExp == Posi.FROM) sf.Geometry = (feature.ShapeCopy as IPolyline).FromPoint;
            else if (currExp == Posi.TO) sf.Geometry = (feature.ShapeCopy as IPolyline).ToPoint;
            IFeatureCursor fc = featureClass.Search(sf, false);

            List<int> polySet = new List<int>(); //所有邻接要素的OID
            IFeature fea = null;
            while ((fea = fc.NextFeature()) != null)
            {
                if (fea.OID != polyID) polySet.Add(fea.OID);
                if (!IsLink(fea, featureClass)) mainCount++;
            }

            if (polySet.Count == 0) // 无邻接边
                return 0;
            else if (polySet.Count == 1) //唯一邻接边
            {
                if (visited.Contains(polySet[0])) //唯一连接路段已名花有主
                    return 0;

                bestPolyID = polySet[0];
                Posi rel = GetRel(bestPolyID, polyID, featureClass);
                if (rel == Posi.FROM) nextExp = Posi.TO;
                else if (rel == Posi.TO) nextExp = Posi.FROM;
                else Console.WriteLine("拓扑出错：[ " + polyID + ", " + bestPolyID + " ]");
                if (IsLink(bestPolyID, featureClass)) return 1;
                else return 2;
            }
            else
            {
                //寻找最匹配段
                double maxAngle = 0; int cddId = -1;
                foreach (int id in polySet)
                {
                    double angle = GetPolylineAngle(polyID, id, featureClass);
                    if (angle > maxAngle)
                    {
                        maxAngle = angle;
                        cddId = id;
                    }
                }

                if (visited.Contains(cddId)) //候选段已名花有主
                    return 0;

                //寻找最佳匹配段的最佳匹配
                polySet.Add(polyID);
                maxAngle = 0; int maxId = -1;
                foreach (int id in polySet)
                {
                    if (id == cddId) continue;
                    double angle = GetPolylineAngle(cddId, id, featureClass);
                    if (angle > maxAngle)
                    {
                        maxAngle = angle;
                        maxId = id;
                    }
                }

                if (maxId == polyID) //配对成功
                {
                    if (!IsLink(cddId, featureClass)) //主要道路
                        if (mainCount == 1) return 2;
                        else return 3;

                    bestPolyID = cddId;
                    Posi rel = GetRel(bestPolyID, polyID, featureClass);
                    if (rel == Posi.FROM) nextExp = Posi.TO;
                    else if (rel == Posi.TO) nextExp = Posi.FROM;
                    else Console.WriteLine("拓扑出错：[ " + polyID + ", " + bestPolyID + " ]");
                    return 1;
                }
                else return 3; //配对不成功
            }
        }

        static Boolean IsLink(int id, IFeatureClass featureClass)
        {
            return IsLink(featureClass.GetFeature(id), featureClass);
        }

        static Boolean IsLink(IFeature feature, IFeatureClass featureClass)
        {
            if (Convert.ToInt32(feature.get_Value(linkIndex)) == 1) return true;
            else return false;
        }

        /// <summary>
        /// 获取Polyline之间的夹角(角度)
        /// </summary>
        /// <param name="id1">Base Polyline的OID</param>
        /// <param name="id2">待比较Polyline的OID</param>
        /// <param name="featureClass">要素类</param>
        static private double GetPolylineAngle(int id1, int id2, IFeatureClass featureClass)
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
        static private double getVetorAngle(double[] vector1, double[] vector2)
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
        static private Posi GetRel(int id1, int id2, IFeatureClass featureClass)
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

        static void AoInitializeFirst()
        {
            try
            {
                m_AoInitialize = new AoInitializeClass();
                esriLicenseStatus licenseStatus = esriLicenseStatus.esriLicenseUnavailable;
                licenseStatus = m_AoInitialize.Initialize(esriLicenseProductCode.esriLicenseProductCodeAdvanced);

                m_AoInitialize.CheckOutExtension(esriLicenseExtensionCode.esriLicenseExtensionCode3DAnalyst);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("ArcEngine 不能正常初始化许可");
            }

        }
    }
}
