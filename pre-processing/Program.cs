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
    class Program
    {
        enum Posi { NONE, FROM, TO };
        static AoInitialize m_AoInitialize;

        //定义全局变量
        //应该保留下来的道路类型
        static public List<string> roadTypeShouldStay = new List<string>()
        {"motorway", "motorway_link", "trunk", "trunk_link", "primary", "primary_link", "secondary", "secondary_link" };
        static public List<string> linkRoadType = new List<string>() { "motorway_link", "trunk_link", "primary_link", "secondary_link" };
        static public int roadTypeIndex = -1; //字段“highway”在属性表中的索引
        static public int id_index = -1;

        static void Main(string[] args)
        {
            ESRI.ArcGIS.RuntimeManager.Bind(ESRI.ArcGIS.ProductCode.EngineOrDesktop);
            AoInitializeFirst();

            if (false) //批量处理
            {
                DirectoryInfo di = new DirectoryInfo(@"E:\\桌面\\道路平行性判断\\CityShapeFile"); //获取文件夹
                DirectoryInfo[] dis = di.GetDirectories(); //获得文件夹内的所有文件夹
                for (int i = 0; i < dis.Length; i++) //遍历文件夹
                {
                    string inPath = dis[i].FullName + "\\highway.shp"; //获取文件夹下面的文件highway.shp
                    if (!File.Exists(inPath)) continue; //不存在文件hiway.shp，跳过
                    Console.WriteLine("正在处理：" + "[" + i + "] " + dis[i].Name);
                    //计时
                    Stopwatch sw = new Stopwatch();
                    sw.Start();

                    //string inPath = "E:\\桌面\\道路平行性判断\\CityShapeFile\\#test\\hk_island_spf.shp";
                    string dir = System.IO.Path.GetDirectoryName(inPath); //可以获得不带文件名的路径
                    string name = System.IO.Path.GetFileNameWithoutExtension(inPath); //可以获得文件名
                    string outPath = dir + "\\" + name + "_spf.shp";

                    IFeatureClass outFeatClass = CopyFeatureClass(inPath, outPath); //拷贝要素类

                    roadTypeIndex = QueryFieldIndex(outFeatClass, "highway");
                    int linkIndex = AddField(outFeatClass, "link", esriFieldType.esriFieldTypeSmallInteger, 1);

                    //打开编辑器
                    IWorkspaceFactory workspaceFactory = new ShapefileWorkspaceFactoryClass();
                    IWorkspace workspace = workspaceFactory.OpenFromFile(System.IO.Path.GetDirectoryName(inPath), 0);
                    IWorkspaceEdit workspaceEdit = workspace as IWorkspaceEdit;
                    workspaceEdit.StartEditing(true);
                    workspaceEdit.StartEditOperation();

                    //按类型删除
                    DeleteFeatureByRoadType(outFeatClass);
                    //为link做标记
                    UpdateTagField(outFeatClass, linkIndex);
                    //删除link道路
                    RemoveLinkRoad(outFeatClass);

                    //停止编辑状态
                    workspaceEdit.StopEditOperation();
                    workspaceEdit.StopEditing(true);

                    //停止计时，输出结果
                    sw.Stop();
                    int featCount = outFeatClass.FeatureCount(null);
                    Console.WriteLine("处理完毕：" + "[" + i + "] " + dis[i].Name + "  规模：" + featCount + "  用时：" + sw.Elapsed.TotalMinutes + "Min");

                }
                Console.ReadKey();
            }
            else
            {
                string inPath = "E:\\桌面\\道路平行性判断\\CityShapeFile\\Hongkong\\highway.shp"; 
                //计时
                Stopwatch sw = new Stopwatch();
                sw.Start();

                //string inPath = "E:\\桌面\\道路平行性判断\\CityShapeFile\\#test\\hk_island_spf.shp";
                string dir = System.IO.Path.GetDirectoryName(inPath); //可以获得不带文件名的路径
                string name = System.IO.Path.GetFileNameWithoutExtension(inPath); //可以获得文件名
                string linkPath = dir + "\\" + name + "_link.shp";
                string spfPath = dir + "\\" + name + "_spf.shp";

                IFeatureClass linkFeatClass = CopyFeatureClass(inPath, linkPath); //拷贝要素类

                roadTypeIndex = QueryFieldIndex(linkFeatClass, "highway");
                int linkIndex = AddField(linkFeatClass, "link", esriFieldType.esriFieldTypeSmallInteger, 1);

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

                //复制图层
                IFeatureClass spfFeatClass = CopyFeatureClass(linkPath, spfPath); //拷贝要素类

                //打开编辑器
                workspaceFactory = new ShapefileWorkspaceFactoryClass();
                workspace = workspaceFactory.OpenFromFile(System.IO.Path.GetDirectoryName(spfPath), 0);
                workspaceEdit = workspace as IWorkspaceEdit;
                workspaceEdit.StartEditing(true);
                workspaceEdit.StartEditOperation();

                //删除link道路
                RemoveLinkRoad(spfFeatClass);

                //停止编辑状态
                workspaceEdit.StopEditOperation();
                workspaceEdit.StopEditing(true);

                //停止计时，输出结果
                sw.Stop();
                int featCount = linkFeatClass.FeatureCount(null);
                Console.WriteLine("规模：" + linkFeatClass.FeatureCount(null) + "  用时：" + Math.Round(sw.Elapsed.TotalMinutes, 2) + "Min");
                Console.ReadKey();
            }
            
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
        /// 按实际情况去除连接道路
        /// <summary>
        /// <param name="featClass">要素类</param>
        static private void RemoveLinkRoad(IFeatureClass featClass)
        {
            string linkWhereClause = GetWhereClause(linkRoadType);
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
                //visited.Add(linkFeatrue.OID);
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

            List<int> lst = new List<int>() { 18, 23, 5 };
            if (lst.Contains(feature.OID))
            {
                Console.WriteLine();
            }

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

        static private int SelfBestFit(int polyID, Posi currExp, IFeatureClass featureClass, out int bestPolyID, out Posi nextExp)
        {
            bestPolyID = -1; nextExp = Posi.NONE;
            int mainCount = 0; //主要道路计数
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
                if (!IsLink(polySet[0], featureClass)) 
                    return 2;
                bestPolyID = polySet[0];
                Posi rel = GetRel(bestPolyID, polyID, featureClass);
                if (rel == Posi.FROM) nextExp = Posi.TO;
                else if (rel == Posi.TO) nextExp = Posi.FROM;
                else Console.WriteLine("拓扑出错：[ " + polyID + ", " + bestPolyID + " ]");
                return 1;
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

                if (IsLink(cddId, featureClass)) //候选段是_link
                {
                    bestPolyID = cddId;
                    Posi rel = GetRel(bestPolyID, polyID, featureClass);
                    if (rel == Posi.FROM) nextExp = Posi.TO;
                    else if (rel == Posi.TO) nextExp = Posi.FROM;
                    else Console.WriteLine("拓扑出错：[ " + polyID + ", " + bestPolyID + " ]");
                    return 1;
                }
                else //候选段是_main
                {
                    if (mainCount == 1) return 2;
                    else return 3;
                }
            }
        }

        static Boolean IsLink(int id, IFeatureClass featureClass)
        {
            return IsLink(featureClass.GetFeature(id), featureClass);
        }

        static Boolean IsLink(IFeature feature, IFeatureClass featureClass)
        {
            if (linkRoadType.Contains(feature.get_Value(roadTypeIndex).ToString())) return true;
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
        /// 计算二维数组第rowIndex行的最大值,返回最大值的索引
        /// </summary>
        /// <param name="rowIndex">行序号</param>
        /// <param name="twoDArray">二维数组</param>
        /// <param name="excludedIndex">该集合里面的列号都不算</param>
        static private int ColIndexOfMaxValue(int rowIndex, double[,] twoDArray, List<int> excludedIndex)
        {
            double maxValue = -1;
            int maxColIndex = -1;
            for (int i = 0; i < twoDArray.GetLength(1); i++)
            {
                if (excludedIndex.Contains(i)) //该点被排除在外
                    continue;
                if (i == rowIndex)
                    continue;
                if (twoDArray[rowIndex, i] > maxValue)
                {
                    maxValue = twoDArray[rowIndex, i];
                    maxColIndex = i;
                }
            }
            return maxColIndex;
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

        /// <summary>
        /// 利用空间关系touch进行空间过滤，找到与point接触的要素，并返回这些要素中主要道路的数量
        /// <summary>
        /// <param name="mainCount">返回对象，主要道路计数</param>
        /// <param name="featureSet">返回对象，与点接触的线要素集合</param>
        /// <param name="point">stroke的端点</param>
        /// <param name="strokeFeature">stroke中的要素集合</param>
        /// <param name="featClass">要素类</param>
        static private List<IFeature> SpatialFilterByPoint(IPoint point, List<int> strokeFeature, IFeatureClass featClass, List<int> visited)
        {
            List<IFeature> featureSet = new List<IFeature>(); //与point接触的要素集合
            //起点查询
            ISpatialFilter filter = new SpatialFilterClass();
            filter.Geometry = point;
            filter.SpatialRel = esriSpatialRelEnum.esriSpatialRelTouches;
            IFeatureCursor searchCursor = featClass.Search(filter, false);

            IFeature feature = null;
            while ((feature = searchCursor.NextFeature()) != null)
                if (!strokeFeature.Contains(feature.OID) && !visited.Contains(feature.OID))
                    featureSet.Add(feature);
            Marshal.FinalReleaseComObject(searchCursor);
            return featureSet;
        }

        /// <summary>
        /// 检查stroke的两个端点，返回是否删除stroke
        /// <summary>
        /// <param name="stroke">自然道路 IPolyline</param>
        /// <param name="featClass">要素类</param>
        static private Boolean EndPointCheck(IPolyline stroke, IFeatureClass featClass)
        {
            if (EndPointCheck(stroke.FromPoint, featClass) || EndPointCheck(stroke.FromPoint, featClass))
                return false; //不可删除
            else return true; //可删除
        }

        /// <summary>
        /// 检查端点，返回是否删除stroke
        /// <summary>
        /// <param name="point">stroke的一个端点</param>
        /// <param name="featClass">要素类</param>
        static private Boolean  EndPointCheck(IPoint point, IFeatureClass featClass)
        {
            int count = 0; //主路段计数，待返回值
            //起点查询
            ISpatialFilter filter = new SpatialFilterClass();
            filter.Geometry = point;
            filter.SpatialRel = esriSpatialRelEnum.esriSpatialRelTouches;
            IFeatureCursor searchCursor = featClass.Search(filter, false);

            IFeature fea = null;
            while ((fea = searchCursor.NextFeature()) != null)
            {
                if (!linkRoadType.Contains(fea.get_Value(roadTypeIndex).ToString()))
                    count++;
            }
            Marshal.FinalReleaseComObject(searchCursor);
            if (count == 1) return true;
            else return false;
        }

        /// <summary>
        /// Every Best Fit原则获得stroke的pos端（from/to）的最平滑Polyline对应的要素
        /// <summary>
        /// <param name="featureSet">待选要素集</param>
        /// <param name="stroke">自然道路IPolyline</param>
        /// <param name="pos">位置（stroke的哪一端from/to）</param>
        static private IFeature EveryBestFit(List<IFeature> featureSet, IPolyline stroke, string pos)
        {
            //self_best_fit原则选取线条
            IFeature bestFeature = null; //最平滑的IFeature，待返回对象

            if (featureSet.Count == 0)
                return bestFeature;

            if (featureSet.Count == 0)
                return featureSet[0];

            //在stroke选取接触点，计算向量
            IPoint touchingPoint;  //接触点：有可能是stroke的FromPoint，也可能是ToPoint
            ISegment seg; //接触点所在的Segment
            double[] vector1; //seg对应的向量
            if (pos == "from") //如果接触点在stroke的From端
            {
                touchingPoint = stroke.FromPoint;
                seg = (stroke as ISegmentCollection).get_Segment(0); //在stroke侧的segment
                vector1 = new double[2] { seg.ToPoint.X - seg.FromPoint.X, seg.ToPoint.Y - seg.FromPoint.Y }; //stroke侧的向量
            }
            else if (pos == "to") //如果接触点在stroke的From端
            {
                touchingPoint = stroke.ToPoint;
                seg = (stroke as ISegmentCollection).get_Segment((stroke as ISegmentCollection).SegmentCount - 1); //在stroke侧的segment
                vector1 = new double[2] { seg.FromPoint.X - seg.ToPoint.X, seg.FromPoint.Y - seg.ToPoint.Y }; //stroke侧的向量
            }
            else return bestFeature; //返回null

            //计算最大偏角的最平滑要要素
            double maxAngle = -1; //最大偏角
            List<double[]> vectorSet = new List<double[]> { }; //记录靠近touchingPoint的Segment对应的向量
            double[] finalVector = null; //bestFeature端邻接着touchingPoint的向量
            for (int i = 0; i < featureSet.Count; i++) //依次取出候选集中的所有要素，
            {
                IFeature fea = featureSet[i];
                ISegmentCollection segColl = fea.ShapeCopy as ISegmentCollection;
                IPoint fromPoint = (fea.ShapeCopy as IPolyline).FromPoint;
                IPoint toPoint = (fea.ShapeCopy as IPolyline).ToPoint;
                double[] vector2 = null;
                if (PointMatch(touchingPoint, fromPoint)) //候选要素的fromPoint端与接触点重合
                {
                    ISegment fromSeg = segColl.get_Segment(0); //取出候选要素的from端的segment，并
                    vector2 = new double[2] { fromSeg.ToPoint.X - fromSeg.FromPoint.X, fromSeg.ToPoint.Y - fromSeg.FromPoint.Y }; //求其向量
                    vectorSet.Add(vector2);
                }
                else //否则，候选要素的to端的segment与接触点重合
                {
                    ISegment toSeg = segColl.get_Segment(segColl.SegmentCount - 1); //取出候选要素的to端的segment，并
                    vector2 = new double[2] { toSeg.FromPoint.X - toSeg.ToPoint.X, toSeg.FromPoint.Y - toSeg.ToPoint.Y }; //求其向量
                    vectorSet.Add(vector2);
                }

                double angle = getAngle(vector1, vector2); //求stroke端的向量与候选要素端的夹角
                if (angle > maxAngle) //取其最大夹角
                {
                    maxAngle = angle;
                    bestFeature = fea; //获取最大夹角要素
                    finalVector = vector2;
                }
            }

            //互认过程：判断bestFeature的bestFeature是否feature
            for (int i = 0; i < featureSet.Count; i++)
            {
                IFeature fea = featureSet[i];
                if (fea.OID == bestFeature.OID) //自身不与自身比
                    continue;
                double[] vector = vectorSet[i];
                double angle = getAngle(finalVector, vector); //求bestFeature跟其它要素的夹角
                if (angle > maxAngle) return null; //如果夹角大于bestFeature跟stroke的夹角，则说明延申无效，返回null
            }

            return bestFeature;
        }

        /// <summary>
        /// Self Best Fit原则获得stroke的pos端（from/to）的最平滑Polyline对应的要素
        /// <summary>
        /// <param name="featureSet">待选要素集</param>
        /// <param name="stroke">自然道路IPolyline</param>
        /// <param name="pos">位置（stroke的哪一端from/to）</param>
        static private IFeature SelfBestFit(List<IFeature> featureSet, IPolyline stroke, string pos) 
        {
            //self_best_fit原则选取线条
            IFeature bestFeature = null; //最平滑的IFeature，待返回对象

            if (featureSet.Count == 0)
                return bestFeature;

            if (featureSet.Count == 0)
                return featureSet[0];

            //在stroke选取接触点，计算向量
            IPoint touchingPoint;  //接触点：有可能是stroke的FromPoint，也可能是ToPoint
            ISegment seg; //接触点所在的Segment
            double[] vector1; //seg对应的向量
            if (pos == "from") //如果接触点在stroke的From端
            {
                touchingPoint = stroke.FromPoint;
                seg = (stroke as ISegmentCollection).get_Segment(0); //在stroke侧的segment
                vector1 = new double[2] { seg.ToPoint.X - seg.FromPoint.X, seg.ToPoint.Y - seg.FromPoint.Y }; //stroke侧的向量
            }
            else if (pos == "to") //如果接触点在stroke的From端
            {
                touchingPoint = stroke.ToPoint;
                seg = (stroke as ISegmentCollection).get_Segment((stroke as ISegmentCollection).SegmentCount - 1); //在stroke侧的segment
                vector1 = new double[2] { seg.FromPoint.X - seg.ToPoint.X, seg.FromPoint.Y - seg.ToPoint.Y }; //stroke侧的向量
            }
            else return bestFeature; //返回null
            
            //计算最大偏角的最平滑要要素
            double maxAngle = -1; //最大偏角
            for (int i = 0; i < featureSet.Count; i++) //依次取出候选集中的所有要素，
            {
                IFeature fea = featureSet[i];
                ISegmentCollection segColl = fea.ShapeCopy as ISegmentCollection;
                IPoint fromPoint = (fea.ShapeCopy as IPolyline).FromPoint;
                IPoint toPoint = (fea.ShapeCopy as IPolyline).ToPoint;
                double[] vector2 = null;
                if (PointMatch(touchingPoint, fromPoint)) //候选要素的fromPoint端与接触点重合
                {
                    ISegment fromSeg = segColl.get_Segment(0); //取出候选要素的from端的segment，并
                    vector2 = new double[2] { fromSeg.ToPoint.X - fromSeg.FromPoint.X, fromSeg.ToPoint.Y - fromSeg.FromPoint.Y }; //求其向量
                }
                else //否则，候选要素的to端的segment与接触点重合
                {
                    ISegment toSeg = segColl.get_Segment(segColl.SegmentCount - 1); //取出候选要素的to端的segment，并
                    vector2 = new double[2] { toSeg.FromPoint.X - toSeg.ToPoint.X, toSeg.FromPoint.Y - toSeg.ToPoint.Y }; //求其向量
                }

                double angle = getAngle(vector1, vector2); //求stroke端的向量与候选要素端的夹角
                if (angle > maxAngle) //取其最大夹角
                {
                    maxAngle = angle;
                    bestFeature = fea; //获取最大夹角要素
                }
            }
            return bestFeature;
        }

        /// <summary>
        /// 获得两个向量的夹角
        /// <summary>
        /// <param name="vector1">向量1</param>
        /// <param name="vector2">向量2</param>
        static private double getAngle(double[] vector1, double[] vector2)
        {
            double vector1_mul_vector2 = vector1[0] * vector2[0] + vector1[1] * vector2[1];
            double mol_vector1 = Math.Sqrt(vector1[0] * vector1[0] + vector1[1] * vector1[1]);
            double mol_vector2 = Math.Sqrt(vector2[0] * vector2[0] + vector2[1] * vector2[1]);
            double cos = vector1_mul_vector2 / (mol_vector1 * mol_vector2);
            double radius = Math.Acos(cos);
            return radius;
        }

        /// <summary>
        /// 匹配两个点，若是同一个点则返回true
        /// <summary>
        /// <param name="point1">点1</param>
        /// <param name="point2">点2</param>
        static private Boolean PointMatch(IPoint point1, IPoint point2)
        {
            double x_diff = Math.Abs(point1.X - point2.X);
            double y_diff = Math.Abs(point1.Y - point2.Y);
            if (x_diff < 0.001 && y_diff < 0.001)
                return true;
            return false;
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
