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
using ArcGIS.Core.SystemCore;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Core.UnitFormats;
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
using System.Windows.Input;

namespace ParcelsAddin
{
  internal class COGOUtils
  {
    internal string ConvertNorthAzimuthDecimalDegreesToDisplayUnit(double InDirection, DisplayUnitFormat incomingDirectionFormat)
    {
      if (incomingDirectionFormat == null)
        return "";

      var dirUnitIn = ArcGIS.Core.SystemCore.DirectionUnits.DecimalDegrees;
      var dirTypeIn = ArcGIS.Core.SystemCore.DirectionType.NorthAzimuth;

      var dirTypeOut = ArcGIS.Core.SystemCore.DirectionType.Polar;
      var directionUnitFormat = incomingDirectionFormat.UnitFormat as CIMDirectionFormat;
      int iRounding = directionUnitFormat.DecimalPlaces;

      if (directionUnitFormat.DirectionType == ArcGIS.Core.CIM.DirectionType.NorthAzimuth)
        dirTypeOut = ArcGIS.Core.SystemCore.DirectionType.NorthAzimuth;
      else if (directionUnitFormat.DirectionType == ArcGIS.Core.CIM.DirectionType.SouthAzimuth)
        dirTypeOut = ArcGIS.Core.SystemCore.DirectionType.SouthAzimuth;
      else if (directionUnitFormat.DirectionType == ArcGIS.Core.CIM.DirectionType.Polar)
        dirTypeOut = ArcGIS.Core.SystemCore.DirectionType.Polar;
      else if (directionUnitFormat.DirectionType == ArcGIS.Core.CIM.DirectionType.QuadrantBearing)
        dirTypeOut = ArcGIS.Core.SystemCore.DirectionType.QuadrantBearing;

      var angleMeasurementUnit = incomingDirectionFormat.MeasurementUnit;
      var dirUnitOut = ArcGIS.Core.SystemCore.DirectionUnits.Radians;
      if (angleMeasurementUnit.FactoryCode == 909004)//Degrees Minutes Seconds
        dirUnitOut = ArcGIS.Core.SystemCore.DirectionUnits.DegreesMinutesSeconds;
      else if (angleMeasurementUnit.FactoryCode == 9102)//Decimal Degrees
        dirUnitOut = ArcGIS.Core.SystemCore.DirectionUnits.DecimalDegrees;
      else if (angleMeasurementUnit.FactoryCode == 9105)//Gradians
        dirUnitOut = ArcGIS.Core.SystemCore.DirectionUnits.Gradians;
      else if (angleMeasurementUnit.FactoryCode == 9106)//Gons
        dirUnitOut = ArcGIS.Core.SystemCore.DirectionUnits.Gons;

      var AngConv = DirectionUnitFormatConversion.Instance;
      var ConvDef = new ConversionDefinition()
      {
        DirectionTypeIn = dirTypeIn,
        DirectionUnitsIn = dirUnitIn,
        DirectionTypeOut = dirTypeOut,
        DirectionUnitsOut = dirUnitOut
      };
      var dir = AngConv.ConvertToString(InDirection, iRounding, ConvDef);
      return FormatDirectionDashesToDegMinSecSymbols(dir);
    }

    internal List<Coordinate2D> CompassRuleAdjust(List<Coordinate3D> TraverseCourses, Coordinate2D StartPoint, Coordinate2D EndPoint,
      List<double> RadiusList, List<double> ArclengthList, List<bool> IsMajorList,
       out Coordinate2D MiscloseVector, out double MiscloseRatio, out double COGOArea)
    {
      double dSUM;
      MiscloseRatio = 100000.0;
      COGOArea = 0.0;
      MiscloseVector = GetClosingVector(TraverseCourses, StartPoint, EndPoint, out dSUM);
      if (MiscloseVector.Magnitude > 0.001)
        MiscloseRatio = dSUM / MiscloseVector.Magnitude;

      if (MiscloseRatio > 100000.0)
        MiscloseRatio = 100000.0;

      double dRunningSum = 0.0;
      double dRunningCircularArcArea = 0.0;
      Coordinate2D[] TraversePoints = new Coordinate2D[TraverseCourses.Count]; //from control
      for (int i = 0; i < TraverseCourses.Count; i++)
      {
        Coordinate2D toPoint = new Coordinate2D();
        Coordinate3D vec = TraverseCourses[i];
        dRunningSum += vec.Magnitude;

        double dScale = dRunningSum / dSUM;
        double dXCorrection = MiscloseVector.X * dScale;
        double dYCorrection = MiscloseVector.Y * dScale;

        //================== Cirular Arc Segment Area Calcs ========================
        if (RadiusList[i] != 0.0)
        {
          double dChord = vec.Magnitude;
          double dHalfChord = dChord / 2.0;
          double dRadius = RadiusList[i];
          var rad = Math.Abs(dRadius);

          //area calculations below are based off the minor arc length even for major circular arc area sector, therefore:
          double dArcLength = IsMajorList[i] ? 2.0 * rad * Math.PI - ArclengthList[i] : ArclengthList[i];

          //test edge case of half circle
          double circArcLength = Math.Abs(2.0 * rad - dChord) > 0.0000001 ? dArcLength : rad * Math.PI;
          double dAreaSector = 0.5 * circArcLength * dRadius;
          double dH = Math.Sqrt((dRadius * dRadius) - (dHalfChord * dHalfChord));
          double dAreaTriangle = dH * dHalfChord;
          double dAreaSegment = Math.Abs(dAreaSector) - Math.Abs(dAreaTriangle);

          if (IsMajorList[i])
          {
            //if it's the major arc we need to take the complement area
            double dCircArcArea = Math.PI * dRadius * dRadius;
            dAreaSegment = dCircArcArea - dAreaSegment;
          }
          if (dRadius < 0.0)
            dAreaSegment = -dAreaSegment;
          dRunningCircularArcArea += dAreaSegment;
        }
        //=======================================================
        toPoint.SetComponents(StartPoint.X + vec.X, StartPoint.Y + vec.Y);
        StartPoint.SetComponents(toPoint.X, toPoint.Y); //re-set the start point to the one just added

        Coordinate2D pAdjustedPoint = new Coordinate2D(toPoint.X - dXCorrection, toPoint.Y - dYCorrection);
        TraversePoints[i] = pAdjustedPoint;
      }

      //================== Area Calcs =============================
      try
      {
        var polygon = PolygonBuilderEx.CreatePolygon(TraversePoints);
        COGOArea = polygon.Area + dRunningCircularArcArea;
      }
      catch
      { return null; }
      //===========================================================

      return TraversePoints.ToList();
    }

    private static Coordinate2D GetClosingVector(List<Coordinate3D> TraverseCourses, Coordinate2D StartPoint,
    Coordinate2D EndPoint, out double SUMofLengths)
    {
      Coordinate3D SumVec = new(0.0, 0.0, 0.0);
      SUMofLengths = 0.0;
      for (int i = 0; i < TraverseCourses.Count - 1; i++)
      {
        if (i == 0)
        {
          SUMofLengths = TraverseCourses[0].Magnitude + TraverseCourses[1].Magnitude;
          SumVec = TraverseCourses[0].AddCoordinate3D(TraverseCourses[1]);
        }
        else
        {
          Coordinate3D SumVec3D = SumVec;
          SUMofLengths += TraverseCourses[i + 1].Magnitude;
          SumVec = SumVec3D.AddCoordinate3D(TraverseCourses[i + 1]);
        }
      }

      double dCalcedEndX = StartPoint.X + SumVec.X;
      double dCalcedEndY = StartPoint.Y + SumVec.Y;

      Coordinate2D CloseVector = new();
      CloseVector.SetComponents(dCalcedEndX - EndPoint.X, dCalcedEndY - EndPoint.Y);
      return CloseVector;
    }

    internal static string FormatDirectionDashesToDegMinSecSymbols(string Bearing)
    {
      string InitialBearingString = Bearing;
      Bearing = Bearing.ToUpper().Trim();
      try
      {
        Bearing = Bearing.Replace(" ", "");
        if (Bearing.EndsWith("E") || Bearing.EndsWith("W"))
          Bearing = Bearing.Insert(Bearing.Length - 1, "\"");
        else
          Bearing = Bearing.Insert(Bearing.Length, "\"");
        int i = Bearing.LastIndexOf('-');

        if (i > -1)
        {
          Bearing = Bearing.Insert(i, "'");
          i = Bearing.IndexOf('-');
          Bearing = Bearing.Insert(i, "°");
          Bearing = Bearing.Replace("-", "");
        }
        else if (i == -1)
          Bearing = Bearing.Replace("\"", "°");
      }
      catch
      {
        return InitialBearingString;
      }
      return Bearing;
    }

  }
  internal class ParcelUtils
  {
    internal double ClockwiseDownStreamEdgePosition(ParcelLineInfo line)
    {
      return line.IsReversed ? line.EndPositionOnParcelEdge : line.StartPositionOnParcelEdge;
    }

    internal double ClockwiseUpStreamEdgePosition(ParcelLineInfo line)
    {
      return line.IsReversed ? line.StartPositionOnParcelEdge : line.EndPositionOnParcelEdge;
    }

    internal bool ParcelEdgeAnalysis(ParcelEdgeCollection parcelEdgeCollection, out bool isClosedLoop,
      out bool allLinesHaveCOGO, out object[] traverseInfo)
    {
      //traverseInfo object list items:
      //1. vector, 2. direction, 3. distance,
      //4. radius, 5. arclength, 6. isMajor, 7. isLineReversed

      var vectorChord = new List<object>();
      var directionList = new List<object>();
      var distanceList = new List<object>();

      var radiusList = new List<object>();
      var arcLengthList = new List<object>();

      var isMajorList = new List<bool>();
      var isLineReversedList = new List<bool>();
      try
      {
        isClosedLoop = true; //start optimistic
        allLinesHaveCOGO = true; //start optimistic
        foreach (var edge in parcelEdgeCollection.Edges)
        {
          var highestPosition = 0.0;
          foreach (var myLineInfo in edge.Lines)
          {
            //test for COGO attributes and line type
            bool hasCOGODirection = myLineInfo.FeatureAttributes.TryGetValue("Direction", out object direction);
            bool hasCOGODistance = myLineInfo.FeatureAttributes.TryGetValue("Distance", out object distance);
            bool hasCOGORadius = myLineInfo.FeatureAttributes.TryGetValue("Radius", out object radius);
            bool hasCOGOArclength = myLineInfo.FeatureAttributes.TryGetValue("ArcLength", out object arclength);
            bool bIsCOGOLine = hasCOGODirection && hasCOGODistance;

            //logic to exclude unwanted lines on this edge
            //if (!myLineInfo.HasNextLineConnectivity)
            //  continue;
            if (myLineInfo.EndPositionOnParcelEdge > 1.0)
              continue;
            if (myLineInfo.EndPositionOnParcelEdge < 0.0)
              continue;
            if (myLineInfo.StartPositionOnParcelEdge > 1.0)
              continue;
            if (myLineInfo.StartPositionOnParcelEdge < 0.0)
              continue;
            //also exclude historic lines
            bool hasRetiredByGuid = myLineInfo.FeatureAttributes.TryGetValue("RetiredByRecord", out object guid);
            if (hasRetiredByGuid && guid != DBNull.Value)
              continue;

            //if we have reached the end of the parcel edge and have cogo attributes,
            //then skip the next lines on this edge
            if (highestPosition == 1.0 && allLinesHaveCOGO)
              continue;

            directionList.Add(direction);
            distanceList.Add(distance);
            isLineReversedList.Add(myLineInfo.IsReversed);

            if (!bIsCOGOLine)
            {//circular arc
              if (hasCOGODirection && hasCOGORadius && hasCOGOArclength) //circular arc
              {
                var dRadius = (double)radius;
                var dArclength = (double)arclength;
                double dCentralAngle = dArclength / dRadius;
                var chordDistance = 2.0 * dRadius * Math.Sin(dCentralAngle / 2.0);
                var flip = myLineInfo.IsReversed ? Math.PI : 0.0;
                var radiansDirection = ((double)direction * Math.PI / 180.0) + flip;
                Coordinate3D vect = new();
                vect.SetPolarComponents(radiansDirection, 0.0, chordDistance);
                if (ClockwiseDownStreamEdgePosition(myLineInfo) == highestPosition)
                {//this line's start matches last line's end
                  vectorChord.Add(vect);
                  arcLengthList.Add(dArclength);
                  if (Math.Abs(dArclength / dRadius) > Math.PI)
                    isMajorList.Add(true);
                  else
                    isMajorList.Add(false);
                  if (myLineInfo.IsReversed)
                    radiusList.Add(-dRadius); //this is for properly calcluating area sector
                  else
                    radiusList.Add(dRadius);
                }
              }
              else //not a cogo circular arc, nor a cogo line 
              {    //partial or no circular arc or line COGO
                allLinesHaveCOGO = false;
                vectorChord.Add(null);

                if (hasCOGORadius)
                  radiusList.Add((double)radius);
                else
                  radiusList.Add(null);

                if (hasCOGOArclength)
                  arcLengthList.Add((double)arclength);
                else
                  arcLengthList.Add(null);
                
                isMajorList.Add(false);
              }
            }
            else //this is a straight cogo line
            {
              var flip = myLineInfo.IsReversed ? Math.PI : 0.0;
              var radiansDirection = ((double)direction * Math.PI / 180.0) + flip;
              Coordinate3D vect = new();
              vect.SetPolarComponents(radiansDirection, 0.0, (double)distance);
              if (ClockwiseDownStreamEdgePosition(myLineInfo) == highestPosition)
              {//this line's start matches previous line's end
                vectorChord.Add(vect);
                arcLengthList.Add(null);
                radiusList.Add(null);
                isMajorList.Add(false);
              }
            }
            var UpstreamPos = ClockwiseUpStreamEdgePosition(myLineInfo);
            highestPosition =
              highestPosition > UpstreamPos ? highestPosition : UpstreamPos;
          }
          if (highestPosition != 1)
          //we lost connectivity, not able to traverse all the way
          //to the end of this edge
          {
            isClosedLoop = false; // no loop connectivity
            //break;
          }
        }
        return true;
      }
      catch
      {
        isClosedLoop = false;
        allLinesHaveCOGO = false;
        return false;
      }
      finally
      {
        traverseInfo = new object[7] {vectorChord, directionList, distanceList,
        radiusList, arcLengthList, isMajorList, isLineReversedList};
      }
    }

    internal bool IsDefaultVersionOnFeatureService(FeatureLayer featureLayer)
    {
      using (Table table = featureLayer.GetTable())
      {
        Datastore datastore = table.GetDatastore();
        Geodatabase geodatabase = datastore as Geodatabase;
        if (geodatabase.IsVersioningSupported())
        {
          using (VersionManager versionManager = geodatabase.GetVersionManager())
          {
            try
            {
              var currentVersion = versionManager.GetCurrentVersion();
              if (currentVersion.GetParent() == null) //default version
                return true;// "Editing on the default version is not available.";
            }
            catch
            {
              return true;
            }
          }
        }
      }
      return false;
    }

    internal bool HasParcelSelection(ParcelLayer parcelLayer)
    {
      bool enableSate = false;
      if (parcelLayer == null)
        return false;
      var parcelTypes = parcelLayer.GetParcelTypeNamesAsync().Result;
      foreach (var parcelType in parcelTypes)
      {
        var fLyrList = parcelLayer.GetParcelPolygonLayerByTypeNameAsync(parcelType).Result.ToList();
        foreach (var fLyr in fLyrList)
        {
          if (fLyr != null && !enableSate)
          {
            enableSate = fLyr.SelectionCount > 0;
            break;
          }
        }
      }
      return enableSate;
    }
  }
}
