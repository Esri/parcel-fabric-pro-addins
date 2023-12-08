/* Copyright 2023 Esri
 *
 * Licensed under the Apache License Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParcelsAddin
{
  internal class CreateRemainder : Button
  {

    //this code below is used for tool enablement, based on parcel selection
    //but is commented out for performance related reason. Fix TBD.

    //protected override void OnUpdate()
    //{
    //  QueuedTask.Run( () =>
    //  {
    //    //confirm we have a license...
    //    if (!ParcelUtils.HasValidLicenseForParcelLayer())
    //    {
    //      this.Enabled = false;
    //      this.DisabledTooltip = "Insufficient license level.";
    //      return;
    //    }

    //    if(Module1.Current.HasParcelPolygonSelection)
    //    {
    //      this.Enabled = true;  //tool is enabled  
    //                            //this.Tooltip = "";
    //    }
    //    else
    //    {
    //      this.Enabled = false;  //tool is disabled  
    //                              //customize your disabledText here
    //      this.DisabledTooltip = "There is no parcel selection.";
    //    }
    //  });
    //}
    protected override async void OnClick()
    {
      bool hasParcelSelection = await QueuedTask.Run(() =>
      {
        var selDict = MapView.Active?.Map?.GetSelection().ToDictionary<FeatureLayer>();
        if (selDict == null)
          return false;
        var polygonLayers =
          selDict.Keys.Where(fl => fl.ShapeType == esriGeometryType.esriGeometryPolygon)
          .Where(fl => fl.IsControlledByParcelFabricAsync(ParcelFabricType.ParcelFabric).Result);
        if (!polygonLayers.Any())
          return false;
        return true;
      });

      if (!hasParcelSelection)
      {
        MessageBox.Show("There is no parcel selection.", this.Caption);
        return;
      }

      string sTargetParcelType = "";
      string sCookieCutterParcelType = "";

      FeatureLayer featSrcLyr = null;
      //jump to the cim thread
      string errorMessage = await QueuedTask.Run(async () =>
      {
        var myParcelFabricLayer =
          MapView.Active?.Map?.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();
        //if there is no fabric in the map then bail
        if (myParcelFabricLayer == null)
          return "There is no fabric layer in the map.";

        var recordsLyr = myParcelFabricLayer.GetRecordsLayerAsync().Result.FirstOrDefault();
        if (recordsLyr == null)
          return "There is no records layer in the map.";
        //...confirm we're not editing default version geodatabase.
        if (ParcelUtils.IsDefaultVersionOnFeatureService(recordsLyr))
          return "Editing on the default version is not available.";

        double _metersPerUnit = 1;
        bool _isPCS = true;
        var pSR = myParcelFabricLayer.GetSpatialReference();
        if (pSR.IsProjected)
          _metersPerUnit = pSR.Unit.ConversionFactor; //meters per unit    
        else
          _isPCS = false;

        //get existing active record
        var pCurrentActiveRec = myParcelFabricLayer.GetActiveRecord();
        long iRecordOID = -1;
        if (pCurrentActiveRec != null)
          iRecordOID=pCurrentActiveRec.ObjectID;

        var parcelTypes = myParcelFabricLayer.GetParcelTypeNamesAsync().Result;
        foreach (var parcelType in parcelTypes)
        {
          var fLyrList = myParcelFabricLayer.GetParcelPolygonLayerByTypeNameAsync(parcelType).Result.ToList();
          foreach (var fLyr in fLyrList)
          {
            if (fLyr != null)
            {
              if (fLyr.SelectionCount > 0)
              {
                if (sCookieCutterParcelType.Trim() == String.Empty || parcelType == sTargetParcelType)
                {// Note the source and target are of the same parcel type.
                 // This is the convention for most record driven parcel workflows.
                  sCookieCutterParcelType = sTargetParcelType = parcelType;
                  featSrcLyr = fLyr;
                }
                else
                  return "All selected parcels must be the same type." + Environment.NewLine +
                "Please select parcels of the same type and try again.";
              }
            }
          }
        }

        //clear historic parcel selection for this parcel type
        foreach (var flyr in myParcelFabricLayer.GetHistoricParcelPolygonLayerByTypeNameAsync(sTargetParcelType).Result.ToList())
          flyr.ClearSelection();

        try
        {
          if (string.IsNullOrEmpty(sTargetParcelType))
            return "";
          var typeNamesEnum = await myParcelFabricLayer.GetParcelPolygonLayerByTypeNameAsync(sTargetParcelType);
          if (typeNamesEnum.Count == 0)
            return "Target parcel type " + sTargetParcelType + " not found. Please try again.";
          var featTargetLyr = typeNamesEnum.FirstOrDefault();

          List<long> ids = new(featSrcLyr.GetSelection().GetObjectIDs());
          var myKVP2 = new KeyValuePair<MapMember, List<long>>(featSrcLyr, ids);
          var sourceParcels = new List<KeyValuePair<MapMember, List<long>>> { myKVP2 };

          //Build the Clip geometry and also Confirm all selected parcels belong to the active record
          var enumSel = featSrcLyr.GetSelection().GetObjectIDs().GetEnumerator();
          List<Polygon> polys = new();
          int iCnt = 0;
          string sid1 = "";
          while (enumSel.MoveNext())
          {
            var pFeatId = enumSel.Current;
            var insp = featSrcLyr.Inspect(pFeatId);
            Polygon poly = (Polygon)insp["SHAPE"];
            polys.Add(poly);
            string sid = insp["CREATEDBYRECORD"].ToString().ToLower();
            if (iCnt == 0)
              sid1 = sid; //get first selected parcel's record
            iCnt++;
            if (sid != sid1)
            {
              string sMsg = "Selected parcels must be in the same record."
              + Environment.NewLine + Environment.NewLine +
              "Please select parcels with the same record and try again.";
              enumSel.Dispose();
              return sMsg;
            }
          }
          if (sid1.Trim() == string.Empty)
          {
            string sMsg = "Selected parcel(s) do not have a record.";
            return sMsg;
          }
          Guid guid = new(sid1);
          if (!await myParcelFabricLayer.SetActiveRecordAsync(guid))
          {
            string sMsg = "Selected parcel(s) record could not be used.";
            return sMsg;
          }
          //union geometry
          var ClipGeometry = GeometryEngine.Instance.Union(polys) as Polygon;
          //use a small negative buffer to avoid pulling in neighbor parcels
          var tol = 0.03 / _metersPerUnit; //3 cms
          if (!_isPCS)
            tol = Math.Atan(tol / (6378100.0 / _metersPerUnit));

          var SearchGeometry = GeometryEngine.Instance.Buffer(ClipGeometry, -tol);//this is used for the search only

          //get the intersecting non-historic parcels of the target type that are in a different record
          SpatialQueryFilter pSpatQu = new();
          pSpatQu.FilterGeometry = SearchGeometry;
          pSpatQu.SpatialRelationship = SpatialRelationship.Intersects;
          pSpatQu.WhereClause = "createdbyrecord <> '" + sid1 + "'" + " AND retiredbyrecord IS NULL";
          List<long> idTargetParcel = new();
          List<Geometry> polysToMerge = new();
          using (RowCursor rowCursor = featTargetLyr.GetFeatureClass().Search(pSpatQu))
          {
            while (rowCursor.MoveNext())
            {
              using Row rowFeat = rowCursor.Current;
              idTargetParcel.Add(rowFeat.GetObjectID());
              polysToMerge.Add((rowFeat as Feature).GetShape());
            }
          }

          if (idTargetParcel.Count == 0)
          {
            string sMsg = "There are no intersecting parcels from a different record."
              + Environment.NewLine
              + Environment.NewLine + "Please select parcels that overlap other parcels from "
              + Environment.NewLine + "a different record and try again.";
            return sMsg;
          }




          //------------------------------------------------------------------------------------------
          ////check for gaps using caterpillar technique 2xtol space 1/2xtol buffer 
          //var bufferTol = 0.4 / _metersPerUnit; //40 cms

          ////merge intersecting parcels- union of the parcel polygons that are getting clipped
          //var mergedCookie = GeometryEngine.Instance.Union(polysToMerge) as Polygon;

          ////Part one: blobify cookie
          //var mergedCookieLines = PolylineBuilderEx.CreatePolyline(mergedCookie);//GeometryEngine.Instance.Union(polysToMerge) as Polyline;
          //var mergedCookieLinesDensified = GeometryEngine.Instance.DensifyByLength(mergedCookieLines, bufferTol * 2.0);
          //var larvaBlobs1 =
          //  GeometryEngine.Instance.Buffer((mergedCookieLinesDensified as Polyline).Points, bufferTol / 2.05);


          ////Part two: blobify clipper
          //var clipGeometryBuffer = GeometryEngine.Instance.Buffer(ClipGeometry, bufferTol) as Polygon;
          //var clipGeometryBufferLines = PolylineBuilderEx.CreatePolyline(clipGeometryBuffer);
          //var clipGeometryBufferLinesDensified =
          //    GeometryEngine.Instance.DensifyByLength(clipGeometryBufferLines, bufferTol * 2.0);
          //var larvaBlobs2 =
          //  GeometryEngine.Instance.Buffer((clipGeometryBufferLinesDensified as Polyline).Points, bufferTol / 2.05);


          ////draw 
          //var polygonSymb = CreatePolygonSymbol(ColorFactory.Instance.BlueRGB, SimpleFillStyle.Solid, 2,
          //  ColorFactory.Instance.GreyRGB);
          //var polylineSymb = CreatePolylineSymbol(ColorFactory.Instance.GreenRGB, 
          //  SimpleLineStyle.Solid, 2.0);

          //var mapV = MapView.Active.AddOverlayAsync(larvaBlobs1, polygonSymb.MakeSymbolReference());
          //MessageBox.Show("larva blobs 1");
          //mapV.Result.Dispose();//clear graphic

          ////larva blobs 2...

          //mapV = MapView.Active.AddOverlayAsync(larvaBlobs2, polygonSymb.MakeSymbolReference());
          //MessageBox.Show("larva blobs 2");
          //mapV.Result.Dispose();//clear graphic


          ////Part three: Merge the blobs

          //var larvaBlobsMerged = GeometryEngine.Instance.Union(larvaBlobs1, larvaBlobs2);
          //mapV = MapView.Active.AddOverlayAsync(larvaBlobsMerged, polygonSymb.MakeSymbolReference());
          //MessageBox.Show("larva blobs merged 1 + 2");
          //mapV.Result.Dispose();//clear graphic

          ////to single parts
          //var larvaBlobSingleParts = 
          //  GeometryEngine.Instance.MultipartToSinglePart(larvaBlobsMerged);
          ////remove simple circles
          ////collect the non-circular pieces
          //List<Polygon> polygons = new();
          //int circCount = 0;
          //foreach (Geometry blob in larvaBlobSingleParts)
          //{
          //  if (Math.Abs((blob as Polygon).Length - (2.0 * Math.PI * bufferTol/2.05)) < 0.01)
          //    circCount++;
          //  else
          //    polygons.Add(blob as Polygon);
          //}

          //var larvaBlobsSingleMerged = GeometryEngine.Instance.Union(polygons);
          //mapV = MapView.Active.AddOverlayAsync(larvaBlobsSingleMerged, polygonSymb.MakeSymbolReference());
          //MessageBox.Show("larva blobs single merged non circles");
          //mapV.Result.Dispose();//clear graphic

          ////this previous step has the intersector

          ////Make some lines from cutter
          //var clipGeometryLines = PolylineBuilderEx.CreatePolyline(ClipGeometry);
          //mapV = MapView.Active.AddOverlayAsync(clipGeometryLines,
          //      polylineSymb.MakeSymbolReference());
          //MessageBox.Show("clip geometry lines");
          //mapV.Result.Dispose();//clear graphic


          //mapV = MapView.Active.AddOverlayAsync(clipGeometryBufferLines,
          //  polylineSymb.MakeSymbolReference());
          //MessageBox.Show("clip geometry buffer lines");
          //mapV.Result.Dispose();//clear graphic

          ////loop through segments and create lines
          //if (!OrthogonalLinesAtBends(clipGeometryBufferLines, bufferTol,out List<Geometry> CutLines))
          //  return "";

          //var crossLines = GeometryEngine.Instance.Union(CutLines);
          //mapV = MapView.Active.AddOverlayAsync(crossLines,
          //  polylineSymb.MakeSymbolReference());
          //MessageBox.Show("cut lines");
          //mapV.Result.Dispose();//clear graphic



          //////to single parts
          ////var larvaBlobs = GeometryEngine.Instance.MultipartToSinglePart(larvaBlobsMerged);



          ////part three: alternate
          ////simplify the inputs for Union.
          //mergedCookieLines = GeometryEngine.Instance.SimplifyPolyline(mergedCookieLines, SimplifyType.Planar);
          //clipGeometryBufferLines = GeometryEngine.Instance.SimplifyPolyline(clipGeometryBufferLines, SimplifyType.Planar);

          //var mergedCookieLinesAndCutterLines =
          //  GeometryEngine.Instance.Union(mergedCookieLines, clipGeometryBufferLines) as Polyline;
          //var mergedCookieLinesAndCutterLinesDensified =
          //  GeometryEngine.Instance.DensifyByLength(mergedCookieLinesAndCutterLines, bufferTol * 2.0);
          //var mergedCookieLinesAndCutterLinesDensifiedBuffered =
          //  GeometryEngine.Instance.Buffer((mergedCookieLinesAndCutterLinesDensified as Polyline).Points, bufferTol / 2.0);
          ////larvaBlobs = GeometryEngine.Instance.MultipartToSinglePart(mergedCookieLinesAndCutterLinesDensifiedBuffered);


          ////var clipGeometryLines = PolylineBuilderEx.CreatePolyline(ClipGeometry);
          ////centipede
          ////var x = GeometryEngine.Instance.ConstructPolygonsFromPolylines();

          ////var y = GeometryEngine.Instance.DensifyByLength(clipGeometryLines, bufferTol * 2.0);

          //var bufferCookie = GeometryEngine.Instance.Buffer(mergedCookie, bufferTol) as Polygon;
          //var bufferClipGeom = GeometryEngine.Instance.Buffer(ClipGeometry, bufferTol) as Polygon;
          //var intersectBuffs = GeometryEngine.Instance.Intersection(bufferCookie, bufferClipGeom) as Polygon;
          //var intersectBuffClipWithMergedCookie =
          //    GeometryEngine.Instance.Intersection(bufferClipGeom, mergedCookie) as Polygon;

          //var symdiffBuffClipWithMergedCookie =
          //    GeometryEngine.Instance.SymmetricDifference(bufferClipGeom, mergedCookie) as Polygon;


          //var gapCheck = GeometryEngine.Instance.MultipartToSinglePart(symdiffBuffClipWithMergedCookie);

          //bool gapFound = false;

          //foreach (var polygon in gapCheck)
          //{
          //  var testForSmallOrNull = GeometryEngine.Instance.Buffer(polygon, -bufferTol / 0.75);
          //  if (testForSmallOrNull == null)
          //  {
          //    gapFound = true;
          //    break;
          //  }
          //  if (testForSmallOrNull.IsEmpty)
          //  {
          //    gapFound = true;
          //    break;
          //  }
          //  var AreaOverLengthRatio = (testForSmallOrNull as Polygon).Area / (testForSmallOrNull as Polygon).Length;
          //  gapFound = AreaOverLengthRatio < 0.09;

          //}

          //////if gap found then...
          ////mapV = MapView.Active.AddOverlayAsync(mergedCookieLinesAndCutterLinesDensifiedBuffered, polySymb.MakeSymbolReference());
          ////MessageBox.Show("graphics test");
          ////mapV.Result.Dispose();//clear graphic

          //--------------------------





          var opRemainder = new EditOperation()
          {
            Name = "Remainder",
            ProgressMessage = "Creating remainder parcel...",
            ShowModalMessageAfterFailure = true,
            SelectNewFeatures = true,
            SelectModifiedFeatures = false
          };
          opRemainder.Clip(featTargetLyr, idTargetParcel, ClipGeometry, ClipMode.DiscardArea);
          if (!opRemainder.Execute())
            return opRemainder.ErrorMessage;
          
          return "";
        }
        catch (Exception ex)
        {
          if (ex.Message.Trim() == string.Empty)
            return "Unspecified error encountered.";
          else
            return ex.Message;
        }
        finally
        {
          //set the active record back to the original
          if (iRecordOID != -1)
            await myParcelFabricLayer.SetActiveRecordAsync(iRecordOID);
          else
            myParcelFabricLayer.ClearActiveRecord();
        }
      });
      if (!string.IsNullOrEmpty(errorMessage))
        MessageBox.Show(errorMessage, "Create remainder parcels");

      //When active record is set, the original parcel is set historic with RetiredByRecord GUID field
      //When active record is set, the new clipped remainder parcel will be tagged with the Active record.

    }

    internal static bool OrthogonalLinesAtBends (Geometry theGeometry, double Distance, out List<Geometry> Lines)
    {
      Lines = new();    
      bool bSegmentsChanged = false;
      if (theGeometry == null)
        return false;

      var partCount = 1;
      var geomPerimLength = 0.0;
      var sumDist = 0.0;

      //check for multi-parts and skip (not implemented)
      if (theGeometry is Polyline)
      {
        partCount = (theGeometry as Polyline).PartCount;
        geomPerimLength = (theGeometry as Polyline).Length;
      }
      if (theGeometry is Polygon)
      {
        partCount = (theGeometry as Polygon).PartCount;
        geomPerimLength = (theGeometry as Polygon).Length;
      }

      if (partCount > 1)
        return false;

      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree
      if (theGeometry.SpatialReference.IsProjected)
        xyTol = theGeometry.SpatialReference.XYTolerance;

      //test for special case where segments share common vertex, "pretzel" skip (not implemented)
      if (HasSegmentsSharingAVertex(theGeometry))
        return false;

      ICollection<Segment> geomSegments = new List<Segment>();
      if (theGeometry is Polyline)
        (theGeometry as Polyline).GetAllSegments(ref geomSegments);
      else if (theGeometry is Polygon)
        (theGeometry as Polygon).GetAllSegments(ref geomSegments);
      else
        return false;

      int iSegCount = geomSegments.Count;
      var lstLineSegments = geomSegments.ToList();
      if (iSegCount < 2)
        return false;

      var longestSeg = 0.0;
      foreach (Segment segment in lstLineSegments)
        longestSeg = (segment.Length > longestSeg) ? segment.Length : longestSeg;

      var envHalfDiagLength =
        Math.Sqrt(Math.Pow(theGeometry.Extent.Width, 2.0) + Math.Pow(theGeometry.Extent.Height, 2.0)) / 2.0;

      for (int i = iSegCount - 1; i > 0; i--)
      {
        var pSeg1 = lstLineSegments[i];
        var pSeg0 = lstLineSegments[i - 1];

        //if a segment length is 0 then skip, the function removes them and saves the feature without
        //stacked vertices. Next time the zero length segments will not be present
        //if (pSeg1.Length == 0.0) - Length property bug for some geometry
        if (ChordDistance(pSeg1) < xyTol * 1.5)
          continue;

        //if (pSeg0.Length == 0.0) - Length property bug for some geometry
        if (ChordDistance(pSeg0) < xyTol * 1.5)
          continue;

        //test that the segments are connected within XY tolerance
        //and confirm that segments run head-to-toe.
        var pt01 = pSeg0.EndCoordinate;
        var pt02 = pSeg1.StartCoordinate;
        var dist = Math.Sqrt(Math.Pow(pt01.X - pt02.X, 2.0) + Math.Pow(pt01.Y - pt02.Y, 2.0));
        if (dist > xyTol * 1.5)
          continue;

        //test that segments are not collapsed to the same point (side case for closed loop polylines)
        var pt03 = pSeg0.StartPoint;
        var pt04 = pSeg1.EndPoint;
        dist = Math.Sqrt(Math.Pow(pt03.X - pt04.X, 2.0) + Math.Pow(pt03.Y - pt04.Y, 2.0));
        if (dist < xyTol * 1.5)
          continue;
        
        sumDist += pSeg1.Length;

        bool Is2CircularArcs;
        bool Is2StraightLines;
        bool Is1StraightLineAnd1CircularcArc;
        if (pSeg0 is EllipticArcSegment && pSeg1 is EllipticArcSegment)
        {
          Is2CircularArcs = true;
          Is2StraightLines = false;
          Is1StraightLineAnd1CircularcArc = false;
          var pCirc0 = pSeg0 as EllipticArcSegment;
          var pCirc1 = pSeg1 as EllipticArcSegment;

          if (pCirc0.IsCounterClockwise && !pCirc1.IsCounterClockwise)
            continue;

          if (!pCirc0.IsCounterClockwise && pCirc1.IsCounterClockwise)
            continue;
        }
        else if (pSeg0.SegmentType == SegmentType.Line && pSeg1.SegmentType == SegmentType.Line)
        {
          Is2CircularArcs = false;
          Is2StraightLines = true;
          Is1StraightLineAnd1CircularcArc=false;
        }
        else
        {
          Is2CircularArcs = false;
          Is2StraightLines = false;
          Is1StraightLineAnd1CircularcArc = true;
        }

        bool segmentsAreTangent =
          IsSegmentPairTangent(pSeg0, pSeg1, default, default, envHalfDiagLength);

        if (!segmentsAreTangent)
        {
          var seg0NAzDirection = COGOUtils.InverseDirectionAsNorthAzimuth(pSeg0.StartCoordinate, 
            pSeg0.EndCoordinate, false);
          var seg1NAzDirection = COGOUtils.InverseDirectionAsNorthAzimuth(pSeg1.StartCoordinate,
            pSeg1.EndCoordinate, false);

          var deltaDirection= COGOUtils.AngleDifferenceBetweenDirections(seg0NAzDirection, 
            seg1NAzDirection);

          if (Math.Abs(deltaDirection) >= 10.0)
          {
            List<Coordinate2D> coordinate2Ds = new();
            var lineEndPoint1 = COGOUtils.PointInDirection(pSeg1.StartCoordinate, 
              seg1NAzDirection + 90.0, Distance);
            var lineEndPoint2 = COGOUtils.PointInDirection(pSeg1.StartCoordinate,
              seg1NAzDirection - 90.0, Distance);
            coordinate2Ds.Add(lineEndPoint1);
            coordinate2Ds.Add(lineEndPoint2);
            var LineGeom = PolylineBuilderEx.CreatePolyline(coordinate2Ds) as Geometry;
            Lines.Add(LineGeom);
          }

        }




        //if (segmentsAreTangent && Is2CircularArcs)
        //{
        //  if (!HasSameCenterPoint(pSeg0, pSeg1))
        //    continue;

        //  var arcOr = ((EllipticArcSegment)pSeg0).IsCounterClockwise ?
        //    ArcOrientation.ArcCounterClockwise : ArcOrientation.ArcClockwise;

        //  //Detect true elliptical arcs
        //  var trueEllipticalArcs = !(((EllipticArcSegment)pSeg0).IsCircular &&
        //   ((EllipticArcSegment)pSeg1).IsCircular);

        //  var arcMinMaj =
        //     Math.Abs(((EllipticArcSegment)pSeg1).CentralAngle) + Math.Abs(((EllipticArcSegment)pSeg0).CentralAngle)
        //     < Math.PI ? MinorOrMajor.Minor : MinorOrMajor.Major;

        //  EllipticArcSegment longerSeg = pSeg0.Length > pSeg1.Length ?
        //    (EllipticArcSegment)pSeg0 : (EllipticArcSegment)pSeg1;

        //  //use a circular arc constructor that ensures the start and end points are the same as for the original feature
        //  EllipticArcSegment pMergedCircularOrEllipticArc;

        //  if (!trueEllipticalArcs)
        //    pMergedCircularOrEllipticArc =
        //      EllipticArcBuilderEx.CreateCircularArc(pSeg0.StartPoint, pSeg1.EndPoint, longerSeg.CenterPoint, arcOr);
        //  else
        //    pMergedCircularOrEllipticArc =
        //    EllipticArcBuilderEx.CreateEllipticArcSegment(pSeg0.StartPoint, pSeg1.EndPoint,
        //    longerSeg.SemiMajorAxis, longerSeg.MinorMajorRatio, longerSeg.RotationAngle, arcMinMaj, arcOr);

        //  //Replace two segments with one
        //  lstLineSegments.RemoveRange(i - 1, 2);
        //  lstLineSegments.Insert(i - 1, pMergedCircularOrEllipticArc);
        //  bSegmentsChanged = true;
        //  //VertexRemoveCount++;
        //}

        //if (segmentsAreTangent && Is2StraightLines)
        //{
        //  var pMergedLine = LineBuilderEx.CreateLineSegment(pSeg0.StartPoint, pSeg1.EndPoint);
        //  //Replace two segments with one
        //  lstLineSegments.RemoveRange(i - 1, 2);
        //  lstLineSegments.Insert(i - 1, pMergedLine);
        //  bSegmentsChanged = true;
        //  //VertexRemoveCount++;
        //}


      }
      ////update the geometry if the segments changed
      //if (bSegmentsChanged)
      //{
      //  bool bPartCountCheck;
      //  bool bPerimeterCheck;
      //  if (theGeometry is Polyline)
      //  {
      //    var polyline = PolylineBuilderEx.CreatePolyline(lstLineSegments);
      //    if (!GeometryEngine.Instance.IsSimpleAsFeature(polyline))
      //      polyline = GeometryEngine.Instance.SimplifyAsFeature(polyline) as Polyline;
      //    bPartCountCheck = polyline.PartCount - partCount == 0;
      //    bPerimeterCheck = polyline.Length / geomPerimLength > 0.9;
      //    if (bPartCountCheck && bPerimeterCheck)
      //      theGeometry = polyline;
      //    else
      //      return false;
      //  }
      //  else if (theGeometry is Polygon)
      //  {
      //    var polygon = PolygonBuilderEx.CreatePolygon(lstLineSegments);
      //    if (!GeometryEngine.Instance.IsSimpleAsFeature(polygon))
      //      polygon = GeometryEngine.Instance.SimplifyAsFeature(polygon) as Polygon;
      //    bPartCountCheck = polygon.PartCount - partCount == 0;
      //    bPerimeterCheck = polygon.Length / geomPerimLength > 0.9;
      //    if (bPartCountCheck && bPerimeterCheck)
      //      theGeometry = polygon;
      //    else
      //      return false;
      //  }
      //}
      return true;
    }

    internal static bool HasSameCenterPoint(Segment seg1, Segment seg2)
    {
      if (seg1.SegmentType != SegmentType.EllipticArc || seg2.SegmentType != SegmentType.EllipticArc)
        return false;

      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree

      if (seg1.SpatialReference.IsProjected)
        xyTol = seg1.SpatialReference.XYTolerance;

      double baseTolerance = xyTol * 10.0;
      var shorterCircArc = seg1.Length <= seg2.Length ? seg1 as EllipticArcSegment : seg2 as EllipticArcSegment;

      double circArcDelta = shorterCircArc.CentralAngle;

      //check for flat circular arcs central angle less than 1°, length less than 1 meter, with same CW/CCW orientation
      //return center point = true, since prior functions
      if (Math.Abs(shorterCircArc.CentralAngle) < (1.0 / 180.0 * Math.PI) && ChordDistance(shorterCircArc) < xyTol * 1000)
        return (seg1 as EllipticArcSegment).IsCounterClockwise == (seg2 as EllipticArcSegment).IsCounterClockwise;

      //take care of edge cases -3° to 3° and 177° to 183°
      if (Math.Abs(circArcDelta - Math.PI) < (3.0 * Math.PI / 180.0) ||
          Math.Abs(circArcDelta) < (3.0 * Math.PI / 180.0))
        circArcDelta = 3.0 * Math.PI / 180.0;

      double dX = Math.Abs(xyTol * (1.0 / Math.Sin(circArcDelta)));
      double precisionNoiseFactor = dX * Math.Log(Math.Abs(shorterCircArc.SemiMajorAxis));

      double radiusTolerance = baseTolerance + precisionNoiseFactor;
      if (radiusTolerance < xyTol)
        radiusTolerance = xyTol;

      var r1 = Math.Abs((seg1 as EllipticArcSegment).SemiMajorAxis);
      var r2 = Math.Abs((seg2 as EllipticArcSegment).SemiMajorAxis);
      double testRadiusDifference = Math.Abs(r2 - r1);

      //test the distance between center points to confirm the same side of circular arc
      double ratio = Math.Abs(r1 / 100.0); //1 percent of radius; only a course-grained check needed
      double centerPointTolerance = Math.Abs(shorterCircArc.SemiMajorAxis * ratio);
      var cp1 = (seg1 as EllipticArcSegment).CenterPoint;
      var cp2 = (seg2 as EllipticArcSegment).CenterPoint;
      var testDist = LineBuilderEx.CreateLineSegment(cp1, cp2).Length;

      return testRadiusDifference <= radiusTolerance && testDist <= centerPointTolerance;
    }
    internal static bool HasSegmentsSharingAVertex(Geometry geom)
    {
      //test for special case where segments share common vertex, "pretzel"
      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree
      double _metersPerUnitDataset = 1.0;

      string sDecPrecFmt = "F9";

      if (geom.SpatialReference.IsProjected)
      {
        xyTol = geom.SpatialReference.XYTolerance;
        _metersPerUnitDataset = geom.SpatialReference.Unit.ConversionFactor;
        sDecPrecFmt = "F3";
      }

      ReadOnlyPointCollection ptColl = null;
      if (geom is Polyline)
        ptColl = (geom as Polyline).Points;
      else if (geom is Polygon)
        ptColl = (geom as Polygon).Points;
      else
        return false;

      List<string> lst = new();
      foreach (var pt in ptColl)
        lst.Add(pt.X.ToString(sDecPrecFmt) + "," + pt.Y.ToString(sDecPrecFmt));

      lst.RemoveAt(lst.Count - 1);
      lst.RemoveAt(0);

      var query = lst.GroupBy(x => x)
              .Where(g => g.Count() > 1)
              .Select(y => y.Key)
              .ToList();

      return query.Count > 0;
    }

    internal static bool IsSegmentPairTangent(Segment seg1, Segment seg2,
      double MinOffsetToleranceInMeters = 0.0, double MaxOffsetToleranceInMeters = 0.0,
      double MaxFeatureLengthInMeters = 0.0, double OffsetRatio = 250.0)
    {
      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree
      double _metersPerUnitDataset = 1.0;

      if (seg1.SpatialReference.IsProjected)
      {
        xyTol = seg1.SpatialReference.XYTolerance;
        _metersPerUnitDataset = seg1.SpatialReference.Unit.ConversionFactor;
      }

      // ---- Workaround for geometry bug 3.0 and 3.1 ----
      if (seg1 is EllipticArcSegment)
        seg1 = ShortCircularArcSegmentCheckAndRepair(seg1 as EllipticArcSegment, xyTol);
      if (seg2 is EllipticArcSegment)
        seg2 = ShortCircularArcSegmentCheckAndRepair(seg2 as EllipticArcSegment, xyTol);
      //---------

      if (MaxFeatureLengthInMeters == 0.0)
        MaxFeatureLengthInMeters = seg1.Length >= seg2.Length ? seg1.Length * _metersPerUnitDataset : seg2.Length * _metersPerUnitDataset;

      if (seg1 is EllipticArcSegment) //convert it to tangent line segment equivalent
      {
        seg1 = GeometryEngine.Instance.QueryTangent(seg1, SegmentExtensionType.ExtendTangentAtTo,
          1.0, AsRatioOrLength.AsRatio, seg1.Length);
      }
      if (seg2 is EllipticArcSegment) //convert it to tangent line segment equivalent
      {
        seg2 = GeometryEngine.Instance.QueryTangent(seg2, SegmentExtensionType.ExtendTangentAtFrom,
          0.0, AsRatioOrLength.AsRatio, seg2.Length);
      }
      var minO = xyTol * 20.0; // 2cms default
      var maxO = xyTol * 2000.0; // 200cms default

      var oRP = Math.Abs(OffsetRatio);
      if (oRP < 10.0)
        oRP = 10.0;

      if (MinOffsetToleranceInMeters != 0.0)
        minO = MinOffsetToleranceInMeters;

      if (MaxOffsetToleranceInMeters != 0.0)
        maxO = MaxOffsetToleranceInMeters;

      var pointA = seg1.StartCoordinate;
      var pointB = seg1.EndCoordinate;
      var pointC = seg2.EndCoordinate;

      //straight line ac (long line)
      var lineACVec = new Coordinate3D(pointC.X - pointA.X, pointC.Y - pointA.Y, 0);
      var lineACUnitVec = new Coordinate3D();//unit vector
      lineACUnitVec.SetPolarComponents(lineACVec.Azimuth, 0, 1.0);
      //====
      //straight line ab (short line a to test point b)
      var lineABVec = new Coordinate3D(pointB.X - pointA.X, pointB.Y - pointA.Y, 0);
      var lineABUnitVec = new Coordinate3D();//unit vector
      lineABUnitVec.SetPolarComponents(lineABVec.Azimuth, 0, 1.0);
      //====
      //straight line bc (short line c to test point b)
      var lineBCVec = new Coordinate3D(pointC.X - pointB.X, pointC.Y - pointB.Y, 0);
      var lineBCUnitVec = new Coordinate3D();//unit vector
      lineBCUnitVec.SetPolarComponents(lineBCVec.Azimuth, 0, 1.0);
      //====

      var dotProd = lineACUnitVec.DotProduct(lineABUnitVec);
      var angBAC = Math.Acos(dotProd);

      var mB = Math.Abs(Math.Sin(angBAC) * lineABVec.Magnitude * _metersPerUnitDataset);
      var mA = Math.Abs(Math.Cos(angBAC) * lineABVec.Magnitude * _metersPerUnitDataset);

      if (mB == 0)
        return true; //if perpendicular offset distance is zero, then segments are tangent

      //Check for segment deflections greater than 45°
      //Considered to be a bend regardless of segment lengths
      var inDegs = angBAC * 180.0 / Math.PI;
      if (inDegs > 45)
        return false;

      dotProd = lineACUnitVec.DotProduct(lineBCUnitVec);
      var angBCA = Math.Acos(dotProd);

      var mC = Math.Abs(Math.Cos(angBCA) * lineBCVec.Magnitude * _metersPerUnitDataset);

      var z = (MaxFeatureLengthInMeters > lineACVec.Magnitude * _metersPerUnitDataset) ?
        Math.Log(MaxFeatureLengthInMeters / lineACVec.Magnitude * _metersPerUnitDataset) : Math.Log(lineACVec.Magnitude * MaxFeatureLengthInMeters);

      var lengthContextMinO = minO * z;

      if (lengthContextMinO < minO / 2.0)
        lengthContextMinO = minO / 2.0; // 1 cm is the smallest allowable offset.

      var R = (mC < mA) ? mC / mB : mA / mB;
      bool iAmABend = (R <= oRP && mB >= lengthContextMinO) || mB >= maxO;
      return !iAmABend; //tangent if not a bend
    }

    private static double ChordDistance(Segment segment)
    {
      if (segment == null)
        return 0.0;
      var pt01 = segment.StartCoordinate;
      var pt02 = segment.EndCoordinate;
      return Math.Sqrt(Math.Pow(pt01.X - pt02.X, 2.0) + Math.Pow(pt01.Y - pt02.Y, 2.0));
    }

    private static Segment ShortCircularArcSegmentCheckAndRepair(EllipticArcSegment segment, double xyTol)
    {
      var pt01 = segment.StartCoordinate;
      var pt02 = segment.EndCoordinate;
      var chord = Math.Sqrt(Math.Pow(pt01.X - pt02.X, 2.0) + Math.Pow(pt01.Y - pt02.Y, 2.0));

      if (segment.Length == 0.0 && chord > xyTol)//this should never be true ... geometry bug 
      {
        var point1 = pt01.ToMapPoint();
        var point2 = pt02.ToMapPoint();
        var flatCircularArcRadius = chord / (0.1 * Math.PI / 180.0); //based on 0.1 degree central angle
        var flatCircArc = CreateCircularArcByEndpoints(point1, point2, flatCircularArcRadius, segment.IsCounterClockwise, false);
        if (flatCircArc != null)
          return flatCircArc;
      }
      return segment;
    }

    internal static EllipticArcSegment CreateCircularArcByEndpoints(MapPoint StartPoint, MapPoint EndPoint,
      double Radius, bool IsCounterClockwise, bool IsMajor, double ScaleFactorForRadius = 1.0)
    {
      var pNewSeg = LineBuilderEx.CreateLineSegment(StartPoint, EndPoint);

      double chordDirection = pNewSeg.Angle;
      double dChord = pNewSeg.Length;

      ArcOrientation CCW =
        IsCounterClockwise ? ArcOrientation.ArcCounterClockwise : ArcOrientation.ArcClockwise;
      MinorOrMajor minMaj =
        IsMajor ? MinorOrMajor.Major : MinorOrMajor.Minor;

      double dRadius = Math.Abs(Radius) * ScaleFactorForRadius;

      EllipticArcSegment circArcSegment;
      try
      {
        circArcSegment = EllipticArcBuilderEx.CreateCircularArc(StartPoint, dChord, chordDirection,
        dRadius, CCW, minMaj, null);
        return circArcSegment;
      }
      catch { return null; }
    }


    private static CIMPolygonSymbol CreatePolygonSymbol(CIMColor polygonColor, SimpleFillStyle fillStyle,
      double outlineWidth, CIMColor outlineColor)
    {
      CIMSymbolReference symbolReference = new();
      CIMStroke outLineCIMStroke = 
        new CIMSolidStroke()
      {
        Color = outlineColor,
        Enable = true,
        ColorLocked = true,
        CapStyle = LineCapStyle.Butt,
        JoinStyle = LineJoinStyle.Miter,
        MiterLimit = 10,
        Width = outlineWidth
      };

      { }
      var cimPolygonSymbol = SymbolFactory.Instance.ConstructPolygonSymbol(polygonColor, fillStyle, outLineCIMStroke);
      //symbolReference = cimPolygonSymbol.MakeSymbolReference();

      return cimPolygonSymbol;
    }

    private static CIMLineSymbol CreatePolylineSymbol(CIMColor lineColor, SimpleLineStyle lineStyle,
      double lineWidth)
    {
      var cimLineSymbol = SymbolFactory.Instance.ConstructLineSymbol
        (lineColor, lineWidth, lineStyle);
       return cimLineSymbol;
    }


    private static CIMLineSymbol AssignLineSymbolEndMarkers(CIMLineSymbol lineSymbol, CIMColor lineColor, double lineWidth, 
      bool useEndMarkers)
    {

      CIMMarkerPlacementAtExtremities endMarker = new()
      {
        AngleToLine = true,
        ExtremityPlacement = ExtremityPlacement.Both
      };

      CIMVectorMarker dotMarker = SymbolFactory.Instance.ConstructMarker(lineColor, 1, SimpleMarkerStyle.Circle) as CIMVectorMarker;
      var dotPolySymbol = dotMarker.MarkerGraphics[0].Symbol as CIMPolygonSymbol;
      dotPolySymbol.SymbolLayers[0] = SymbolFactory.Instance.ConstructStroke(lineColor, 0.1, SimpleLineStyle.Solid);      //This is the outline
      dotPolySymbol.SymbolLayers[1] = SymbolFactory.Instance.ConstructSolidFill(lineColor);                               //This is the fill

      dotMarker.MarkerPlacement = endMarker;
      endMarker.ExtremityPlacement = ExtremityPlacement.Both;
      dotMarker.Size = 3;
      dotMarker.AnchorPoint = MapPointBuilderEx.CreateMapPoint(0, 0);
      dotMarker.ColorLocked = true;

      var symLayersEndMarks = new CIMSymbolLayer[]
      {
        new CIMSolidStroke()
        {
            Color = lineColor,
            Enable = true,
            ColorLocked = true,
            CapStyle = LineCapStyle.Butt,
            JoinStyle = LineJoinStyle.Miter,
            MiterLimit = 10,
            Width = lineWidth
        },
        dotMarker
      };
      lineSymbol.SymbolLayers = symLayersEndMarks;
      return lineSymbol;
    }

    internal static Task<CIMPolygonSymbol> CreatePolygonSymbolAsync(CIMColor polygonColor, SimpleFillStyle fillStyle,
      double outlineWidth, CIMColor outlineColor)
    {
      return QueuedTask.Run<CIMPolygonSymbol>(() =>
      {
        CIMSymbolReference symbolReference = new();
        CIMStroke outLineCIMStroke =
          new CIMSolidStroke()
          {
            Color = outlineColor,
            Enable = true,
            ColorLocked = true,
            CapStyle = LineCapStyle.Butt,
            JoinStyle = LineJoinStyle.Miter,
            MiterLimit = 10,
            Width = outlineWidth
          };

        { }
        var cimPolygonSymbol = SymbolFactory.Instance.ConstructPolygonSymbol(polygonColor, fillStyle, outLineCIMStroke);
        return cimPolygonSymbol;
      });
    }

    internal static Task<CIMPointSymbol> CreatePointSymbolAsync()
    {
      return QueuedTask.Run<CIMPointSymbol>(() =>
      {
        var circlePtSymbol = SymbolFactory.Instance.ConstructPointSymbol(ColorFactory.Instance.BlueRGB, 6, SimpleMarkerStyle.Circle);
        //Modifying this point symbol with the attributes we want.
        //getting the marker that is used to render the symbol
        var marker = circlePtSymbol.SymbolLayers[0] as CIMVectorMarker;
        //Getting the polygon symbol layers components in the marker
        var polySymbol = marker.MarkerGraphics[0].Symbol as CIMPolygonSymbol;
        //modifying the polygon's outline and width per requirements
        polySymbol.SymbolLayers[0] = SymbolFactory.Instance.ConstructStroke(ColorFactory.Instance.BlackRGB, 2, SimpleLineStyle.Solid); //This is the outline
        polySymbol.SymbolLayers[1] = SymbolFactory.Instance.ConstructSolidFill(ColorFactory.Instance.BlueRGB); //This is the fill
        return circlePtSymbol;
      });

    }

  }
}
