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

            string inPath = "E:/桌面/道路平行性判断/CityShapeFile/#test/hk_island_dlt.shp";
            string dir = System.IO.Path.GetDirectoryName(inPath); //可以获得不带文件名的路径
            string name = System.IO.Path.GetFileNameWithoutExtension(inPath); //可以获得文件名
            string outPath = dir + "\\" + name + "_spf.shp";
            Console.WriteLine("inPath: " + inPath);
            Console.WriteLine("outPath: " + outPath);

            IFeatureClass outFeatClass = CopyFeatureClass(inPath, outPath); //拷贝要素类

            //为link做标记
            //IFeatureClass inFeatClass = OpenFeatClass(inPath); //待探测尖角的要素类
            //UpdateTagField(inFeatClass, AddField(inFeatClass, "link", esriFieldType.esriFieldTypeSmallInteger, 1));
            
            roadTypeIndex = QueryFieldIndex(outFeatClass, "highway");
            id_index = QueryFieldIndex(outFeatClass, "id");
            //删除无关道路
            //DeleteFeatureByRoadType(outFeatClass);
            //删除link道路
            RemoveLinkRoad(outFeatClass);
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
            Console.WriteLine("linkWhereClause: " + linkWhereClause);
            IFeatureCursor searchCursor = featClass.Search(queryFilter, false);

            IFeature linkFeatrue = null;
            while ((linkFeatrue = searchCursor.NextFeature()) != null)
            {
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
                Console.WriteLine("按类型删除：" + feature.OID + "/" + featCout);
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
            int count = 0;

            IFeature linkFeatrue = null;
            while ((linkFeatrue = searchCursor.NextFeature()) != null)
            {
                Console.WriteLine("删除连接道路：" + count + "/" + featCout);
                count++;

                //左右延伸寻找stroke，并删除该stroke中的所有要素
                DeleteFeatureByStrokeTouching(linkFeatrue, featClass);
            }
            Marshal.FinalReleaseComObject(searchCursor);
        }

        /// <summary>
        /// 获得从某一要素开始延伸的所有要素
        /// <summary>
        /// <param name="feature">要素</param>
        /// <param name="featClass">要素类</param>
        static private void DeleteFeatureByStrokeTouching(IFeature feature, IFeatureClass featClass)
        {
            Console.WriteLine("feature id：" + feature.get_Value(id_index).ToString());
            object missing = Type.Missing;
            List<int> strokeFeature = new List<int>() { feature.OID }; //存储一段stroke的要素
            List<int> fea_id = new List<int>() { Convert.ToInt32(feature.get_Value(id_index))}; //存储一段stroke的要素

            //将feature包装成一段stroke
            IGeometryCollection geoColl = feature.ShapeCopy as IGeometryCollection;
            IPath initPath = geoColl.get_Geometry(0) as IPath;
            IPolyline stroke = new PolylineClass();
            (stroke as IGeometryCollection).AddGeometry(initPath, missing, missing);
            (stroke as ITopologicalOperator).Simplify();

            Boolean fromStopMoving = false; //from端停止前进
            Boolean toStopMoving = false; //to端停止前进

            //沿着feature的polyline前后寻找link道路，将其存储在strokeFeature中，这些道路组合成stroke，
            //如果stroke的两端存在与非link道路的接触，则保留，否则删除
            while (true)
            {
                if (fromStopMoving && toStopMoving) break; //两边都无法通行，则退出

                if(!fromStopMoving)
                {
                    //起点查询
                    ISpatialFilter spatialFilter_From = new SpatialFilterClass();
                    spatialFilter_From.Geometry = stroke.FromPoint;
                    spatialFilter_From.SpatialRel = esriSpatialRelEnum.esriSpatialRelTouches;
                    IFeatureCursor featCursor_From = featClass.Search(spatialFilter_From, false);

                    List<IFeature> fromSide = new List<IFeature>();
                    IFeature feature_From = null;
                    while ((feature_From = featCursor_From.NextFeature()) != null)
                    {
                        if (!strokeFeature.Contains(feature_From.OID))
                        {
                            //Console.WriteLine("加入From：" + Convert.ToInt32(feature_From.get_Value(id_index)));
                            fromSide.Add(feature_From);
                        }
                        
                    }
                    Marshal.FinalReleaseComObject(featCursor_From);

                    if (fromSide .Count == 0) //From端不相接
                        fromStopMoving = true;
                    else if (fromSide.Count == 1) //Form端唯一相接
                    {
                        if (linkRoadType.Contains(fromSide[0].get_Value(roadTypeIndex).ToString())) //唯一相接的是link
                        {
                            IPath path_From = (fromSide[0].ShapeCopy as IGeometryCollection).get_Geometry(0) as IPath;
                            (stroke as IGeometryCollection).AddGeometry(path_From);
                            (stroke as ITopologicalOperator).Simplify();
                            strokeFeature.Insert(0, fromSide[0].OID);

                            fea_id.Insert(0, Convert.ToInt32(fromSide[0].get_Value(id_index)));
                            Console.WriteLine("from_unique: " + Convert.ToInt32(fromSide[0].get_Value(id_index)));
                        }
                        else fromStopMoving = true;
                    }
                    else if (fromSide.Count > 1) //Form端不唯一相接
                    {
                        //self_best_fit原则选取线条
                        IPoint point_From = stroke.FromPoint;
                        ISegment seg_From = (stroke as ISegmentCollection).get_Segment(0);
                        double[] vector1 = new double[2] { seg_From.ToPoint.X - seg_From.FromPoint.X, seg_From.ToPoint.Y - seg_From.FromPoint.Y };

                        double maxAngle = -1;
                        IFeature bestFeature = null;

                        for (int i = 0; i < fromSide.Count; i++)
                        {
                            IFeature fea = fromSide[i];
                            ISegmentCollection segColl = fea.ShapeCopy as ISegmentCollection;
                            IPoint fromPoint = (fea.ShapeCopy as IPolyline).FromPoint;
                            IPoint toPoint = (fea.ShapeCopy as IPolyline).ToPoint;
                            double[] vector2 = null;
                            if (PointMatch(point_From, fromPoint)) //首段相接
                            {
                                ISegment fromSeg = segColl.get_Segment(0);
                                vector2 = new double[2] { fromSeg.ToPoint.X - fromSeg.FromPoint.X, fromSeg.ToPoint.Y - fromSeg.FromPoint.Y };
                            }
                            else //尾端相接
                            {
                                ISegment toSeg = segColl.get_Segment(segColl.SegmentCount - 1);
                                vector2 = new double[2] { toSeg.FromPoint.X - toSeg.ToPoint.X, toSeg.FromPoint.Y - toSeg.ToPoint.Y };
                            }

                            double angle = getAngle(vector1, vector2);
                            if (angle > maxAngle)
                            {
                                maxAngle = angle;
                                bestFeature = fea;
                            }
                        }

                        if (linkRoadType.Contains(bestFeature.get_Value(roadTypeIndex).ToString())) //如果最符合的一段是link
                        {
                            IPath path_From = (bestFeature.ShapeCopy as IGeometryCollection).get_Geometry(0) as IPath;
                            (stroke as IGeometryCollection).AddGeometry(path_From);
                            (stroke as ITopologicalOperator).Simplify();
                            strokeFeature.Insert(0, bestFeature.OID);

                            fea_id.Insert(0, Convert.ToInt32(bestFeature.get_Value(id_index)));
                            Console.WriteLine("from_multi: " + Convert.ToInt32(bestFeature.get_Value(id_index)));
                        }
                        else fromStopMoving = true;
                    }

                    fromSide.Clear();
                }

                if (!toStopMoving)
                {
                    //终点查询
                    ISpatialFilter spatialFilter_To = new SpatialFilterClass();
                    spatialFilter_To.Geometry = stroke.ToPoint;
                    spatialFilter_To.SpatialRel = esriSpatialRelEnum.esriSpatialRelTouches;
                    IFeatureCursor featCursor_To = featClass.Search(spatialFilter_To, false);

                    List<IFeature> toSide = new List<IFeature>();
                    IFeature feature_To = null;
                    while ((feature_To = featCursor_To.NextFeature()) != null)
                    {
                        if (!strokeFeature.Contains(feature_To.OID))
                        {
                            //Console.WriteLine("加入To：" + Convert.ToInt32(feature_To.get_Value(id_index)));
                            toSide.Add(feature_To);
                        }
                    }
                    Marshal.FinalReleaseComObject(featCursor_To);

                    if (toSide.Count == 0) //to端不相接
                        toStopMoving = true;
                    else if (toSide.Count == 1) //to端唯一相接
                    {
                        if (linkRoadType.Contains(toSide[0].get_Value(roadTypeIndex).ToString())) //唯一相接的是link
                        {
                            IPath path_To = (toSide[0].ShapeCopy as IGeometryCollection).get_Geometry(0) as IPath;
                            (stroke as IGeometryCollection).AddGeometry(path_To);
                            (stroke as ITopologicalOperator).Simplify();
                            strokeFeature.Add(toSide[0].OID);

                            fea_id.Add(Convert.ToInt32(toSide[0].get_Value(id_index)));
                            Console.WriteLine("to_unique: " + Convert.ToInt32(toSide[0].get_Value(id_index)));
                        }
                        else toStopMoving = true;
                    }
                    else if (toSide.Count > 1) //to端不唯一相接
                    {
                        //self_best_fit原则选取线条
                        IPoint point_To = stroke.ToPoint;
                        ISegment seg_To = (stroke as ISegmentCollection).get_Segment((stroke as ISegmentCollection).SegmentCount - 1);
                        double[] vector1 = new double[2] { seg_To.FromPoint.X - seg_To.ToPoint.X, seg_To.FromPoint.Y - seg_To.ToPoint.Y };

                        double maxAngle = -1;
                        IFeature bestFeature = null;

                        for (int i = 0; i < toSide.Count; i++)
                        {
                            IFeature fea = toSide[i];
                            ISegmentCollection segColl = fea.ShapeCopy as ISegmentCollection;
                            IPoint fromPoint = (fea.ShapeCopy as IPolyline).FromPoint;
                            IPoint toPoint = (fea.ShapeCopy as IPolyline).ToPoint;
                            double[] vector2 = null;
                            if (PointMatch(point_To, fromPoint)) //首段相接
                            {
                                ISegment fromSeg = segColl.get_Segment(0);
                                vector2 = new double[2] { fromSeg.ToPoint.X - fromSeg.FromPoint.X, fromSeg.ToPoint.Y - fromSeg.FromPoint.Y };
                            }
                            else //尾端相接
                            {
                                ISegment toSeg = segColl.get_Segment(segColl.SegmentCount - 1);
                                vector2 = new double[2] { toSeg.FromPoint.X - toSeg.ToPoint.X, toSeg.FromPoint.Y - toSeg.ToPoint.Y };
                            }

                            double angle = getAngle(vector1, vector2);
                            if (angle > maxAngle)
                            {
                                maxAngle = angle;
                                bestFeature = fea;
                            }
                        }

                        if (linkRoadType.Contains(bestFeature.get_Value(roadTypeIndex).ToString())) //如果最符合的一段是link
                        {
                            IPath path_To = (bestFeature.ShapeCopy as IGeometryCollection).get_Geometry(0) as IPath;
                            (stroke as IGeometryCollection).AddGeometry(path_To);
                            (stroke as ITopologicalOperator).Simplify();
                            strokeFeature.Add(bestFeature.OID);

                            fea_id.Add(Convert.ToInt32(bestFeature.get_Value(id_index)));
                            Console.WriteLine("to_multi: " + Convert.ToInt32(bestFeature.get_Value(id_index)));
                        }
                        else toStopMoving = true;
                    }

                    toSide.Clear();
                }
            }

            //输出strokeFeature
            Console.WriteLine("fea_id：");
            foreach(int id in fea_id)
            {
                Console.Write(id + " ");
            }
            Console.WriteLine();

            //起点查询
            ISpatialFilter sFilter_From = new SpatialFilterClass();
            sFilter_From.Geometry = stroke.FromPoint;
            sFilter_From.SpatialRel = esriSpatialRelEnum.esriSpatialRelTouches;
            IFeatureCursor fCursor_From = featClass.Search(sFilter_From, false);

            int count_From = 0; //非连接路段集合
            IFeature currentFeature_From = null;
            while ((currentFeature_From = fCursor_From.NextFeature()) != null)
            {
                if (!linkRoadType.Contains(currentFeature_From.get_Value(roadTypeIndex).ToString()))
                    count_From++;
            }
            Marshal.FinalReleaseComObject(fCursor_From);

            //终点查询
            ISpatialFilter sFilter_To = new SpatialFilterClass();
            sFilter_From.Geometry = stroke.FromPoint;
            sFilter_From.SpatialRel = esriSpatialRelEnum.esriSpatialRelTouches;
            IFeatureCursor fCursor_To = featClass.Search(sFilter_From, false);

            int count_To = 0; //非连接路段集合
            IFeature currentFeature_To = null;
            while ((currentFeature_To = fCursor_To.NextFeature()) != null)
            {
                if (!linkRoadType.Contains(currentFeature_To.get_Value(roadTypeIndex).ToString()))
                    count_To++;
            }
            Marshal.FinalReleaseComObject(fCursor_To);

            if (count_From == 1 || count_To == 1) { } //stroke的某一端与普通道路唯一相接，则保留，否则
            else //将stroke的组成要素删除
            {
                Console.WriteLine("应该删除");
                foreach (int oid in strokeFeature)
                    featClass.GetFeature(oid).Delete();
            }

            strokeFeature.Clear();
            stroke.SetEmpty();
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
