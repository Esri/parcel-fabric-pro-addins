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
    internal static string ConvertNorthAzimuthDecimalDegreesToDisplayUnit(double InDirection,
      DisplayUnitFormat incomingDirectionFormat, bool ConvertDashesToDMSSymbols = true)
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
      var dir = AngConv.ConvertToString(InDirection, iRounding, ConvDef).Replace(" ", "");
      if (ConvertDashesToDMSSymbols)
      {
        dir = FormatDirectionDashesToDegMinSecSymbols(dir);
        if (dirUnitOut == ArcGIS.Core.SystemCore.DirectionUnits.Gons)
          dir = dir.Replace("°", "g");
      }
      return dir;
    }

    internal static string ConvertPolarRadiansToDisplayUnit(double InDirection,
      DisplayUnitFormat incomingDirectionFormat, bool ConvertDashesToDMSSymbols = true)
    {
      if (incomingDirectionFormat == null)
        return "";

      var dirUnitIn = ArcGIS.Core.SystemCore.DirectionUnits.Radians;
      var dirTypeIn = ArcGIS.Core.SystemCore.DirectionType.Polar;

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
      var dir = AngConv.ConvertToString(InDirection, iRounding, ConvDef).Replace(" ", "");
      if (ConvertDashesToDMSSymbols)
      {
        dir = FormatDirectionDashesToDegMinSecSymbols(dir);
        if (dirUnitOut == ArcGIS.Core.SystemCore.DirectionUnits.Gons)
          dir = dir.Replace("°", "g");
      }
      return dir;
    }

    internal static string ConvertDirectionDifferenceInDecimalDegreesToDisplayUnitAngle(double incomingDirectionDifference,
      DisplayUnitFormat incomingDirectionFormat, bool ConvertDashesToDMSSymbols = true)
    {
      if (incomingDirectionFormat == null)
        return "";

      var signPrefix = incomingDirectionDifference >= 0 ? "+" : "-";
      incomingDirectionDifference = Math.Abs(incomingDirectionDifference);

      var directionUnitFormat = incomingDirectionFormat.UnitFormat as CIMDirectionFormat;
      int iRounding = directionUnitFormat.DecimalPlaces;

      var dirUnitIn = ArcGIS.Core.SystemCore.DirectionUnits.DecimalDegrees;
      var dirUnitOut = ArcGIS.Core.SystemCore.DirectionUnits.DecimalDegrees;
      var dirTypeIn = ArcGIS.Core.SystemCore.DirectionType.Polar;
      var dirTypeOut = ArcGIS.Core.SystemCore.DirectionType.Polar;

      var angleMeasurementUnit = incomingDirectionFormat.MeasurementUnit;
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
      var ang = AngConv.ConvertToString(incomingDirectionDifference, iRounding, ConvDef);

      if (ConvertDashesToDMSSymbols)
      {
        ang = PadMinutesSeconds(ang);
        ang = FormatDirectionDashesToDegMinSecSymbols(ang);
        if (dirUnitOut == ArcGIS.Core.SystemCore.DirectionUnits.Gons)
          ang = ang.Replace("°", "g");
      }
      return signPrefix + ang;
    }

    internal static void GetCOGOLineFeatureLayersSelection(MapView myActiveMapView,
      out Dictionary<FeatureLayer, List<long>> COGOLineSelections)
    {
      List<FeatureLayer> featureLayer = new();
      COGOLineSelections = new();

      var fLyrList = myActiveMapView.Map.GetLayersAsFlattenedList().OfType<FeatureLayer>().
        Where(l => l.GetFeatureClass().GetDefinition().IsCOGOEnabled());

      if (fLyrList == null) return;

      foreach (var fLyr in fLyrList)
      {
        if (fLyr.SelectionCount > 0)
          featureLayer.Add(fLyr);
      }

      foreach (var lyr in featureLayer)
      {
        var fc = lyr.GetFeatureClass();
        List<long> lstOids = new();
        using (RowCursor rowCursor = lyr.GetSelection().Search())
        {
          while (rowCursor.MoveNext())
          {
            using (Row rowFeat = rowCursor.Current)
            {
              if (!COGOLineSelections.ContainsKey(lyr))
                COGOLineSelections.Add(lyr, lstOids);
              lstOids.Add(rowFeat.GetObjectID());
            }
          }
        }
        if (lstOids.Count > 0)
          COGOLineSelections[lyr] = lstOids;
      }
    }

    internal static double ConvertPolarRadiansToNorthAzimuth(double InDirection)
    {
      var dirUnitIn = ArcGIS.Core.SystemCore.DirectionUnits.Radians;
      var dirTypeIn = ArcGIS.Core.SystemCore.DirectionType.Polar;

      var dirTypeOut = ArcGIS.Core.SystemCore.DirectionType.NorthAzimuth;
      var dirUnitOut = ArcGIS.Core.SystemCore.DirectionUnits.DecimalDegrees;

      var AngConv = DirectionUnitFormatConversion.Instance;
      var ConvDef = new ConversionDefinition()
      {
        DirectionTypeIn = dirTypeIn,
        DirectionUnitsIn = dirUnitIn,
        DirectionTypeOut = dirTypeOut,
        DirectionUnitsOut = dirUnitOut
      };
      return AngConv.ConvertToDouble(InDirection, ConvDef);
    }

    internal static string[] GetBackstageDirectionTypeAndUnit(DisplayUnitFormat incomingDirectionFormat, bool ReturnFullName = false)
    {
      if (incomingDirectionFormat == null)
        return new string[2] { "", "" };

      var directionUnitFormat = incomingDirectionFormat.UnitFormat as CIMDirectionFormat;
      string sDirectionType = "";
      string sDirectionUnit = "R";//default to radians
      if (ReturnFullName)
      {
        sDirectionType = directionUnitFormat.DirectionType.ToString();
        sDirectionType = sDirectionType.Replace("tB", "t B").Replace("hA", "h A");

        sDirectionUnit = incomingDirectionFormat.MeasurementUnit.Name;
        sDirectionUnit =
          sDirectionUnit.Replace("Degree", "Degrees").Replace("Minute", "Minutes").Replace("Second", "Seconds");
      }
      else
      {
        if (directionUnitFormat.DirectionType == ArcGIS.Core.CIM.DirectionType.NorthAzimuth)
          sDirectionType = "NA";
        else if (directionUnitFormat.DirectionType == ArcGIS.Core.CIM.DirectionType.SouthAzimuth)
          sDirectionType = "SA";
        else if (directionUnitFormat.DirectionType == ArcGIS.Core.CIM.DirectionType.Polar)
          sDirectionType = "P";
        else if (directionUnitFormat.DirectionType == ArcGIS.Core.CIM.DirectionType.QuadrantBearing)
          sDirectionType = "QB";

        var angleMeasurementUnit = incomingDirectionFormat.MeasurementUnit;
        if (angleMeasurementUnit.FactoryCode == 909004)//Degrees Minutes Seconds
          sDirectionUnit = "DMS";
        else if (angleMeasurementUnit.FactoryCode == 9102)//Decimal Degrees
          sDirectionUnit = "DD";
        else if (angleMeasurementUnit.FactoryCode == 9105)//Gradians
          sDirectionUnit = "G";
        else if (angleMeasurementUnit.FactoryCode == 9106)//Gons
          sDirectionUnit = "G";
      }

      return new string[2] { sDirectionType, sDirectionUnit };
    }

    internal static List<Coordinate2D> CompassRuleAdjust(List<Coordinate3D> TraverseCourses, Coordinate2D StartPoint, Coordinate2D EndPoint,
      List<double> RadiusList, List<double> ArclengthList, List<bool> IsMajorList,
       out Coordinate2D MiscloseVector, out double MiscloseRatio, out double COGOArea)
    {
      MiscloseRatio = 100000.0;
      COGOArea = 0.0;
      MiscloseVector = GetClosingVector(TraverseCourses, StartPoint, EndPoint, out double dSUM);
      if (MiscloseVector.Magnitude > 0.001)
        MiscloseRatio = dSUM / MiscloseVector.Magnitude;

      if (MiscloseRatio > 100000.0)
        MiscloseRatio = 100000.0;

      double dRunningSum = 0.0;
      double dRunningCircularArcArea = 0.0;
      Coordinate2D[] TraversePoints = new Coordinate2D[TraverseCourses.Count]; //from control
      for (int i = 0; i < TraverseCourses.Count; i++)
      {
        Coordinate2D toPoint = new();
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

        Coordinate2D pAdjustedPoint = new(toPoint.X - dXCorrection, toPoint.Y - dYCorrection);
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

    internal static string PadMinutesSeconds(string Angle)
    {
      string InitialAngleString = Angle;
      try
      {
        Angle = Angle.Replace(" ", "");

        int i = Angle.LastIndexOf('-');
        int j = Angle.IndexOf('-');

        if (i - j == 2)
          Angle = Angle.Insert(j + 1, "0");

        i = Angle.LastIndexOf('-');//get it again
        if (Angle.Length - 2 == i)
          Angle = Angle.Insert(i + 1, "0");

      }
      catch
      {
        return InitialAngleString;
      }
      return Angle;
    }

    internal static bool GetCOGOFromGeometry(Polyline myLineFeature, SpatialReference MapSR, double ScaleFactor,
        double DirectionOffset, out object[] COGODirectionDistanceRadiusArcLength)
    {
      COGODirectionDistanceRadiusArcLength =
                          new object[4] { DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value };
      try
      {
        COGODirectionDistanceRadiusArcLength[0] = DBNull.Value;
        COGODirectionDistanceRadiusArcLength[1] = DBNull.Value;

        var GeomSR = myLineFeature.SpatialReference;
        if (GeomSR.IsGeographic && MapSR.IsGeographic)
          return false; //Future work: use API for Geodesics.
        double MapSRMetersPerUnit = 1;
        double DatasetSRMetersPerUnit = 1;

        if (GeomSR.IsProjected)
          DatasetSRMetersPerUnit = GeomSR.Unit.ConversionFactor;

        if (MapSR.IsProjected)
        { //project the data geometry to map spatial reference
            MapSRMetersPerUnit = MapSR.Unit.ConversionFactor; // Meters per unit.
            myLineFeature = GeometryEngine.Instance.Project(myLineFeature, MapSR) as Polyline;
        }

        EllipticArcSegment pCircArc;
        ICollection<Segment> LineSegments = new List<Segment>();
        myLineFeature.GetAllSegments(ref LineSegments);
        int numSegments = LineSegments.Count;

        IList<Segment> iList = LineSegments as IList<Segment>;
        Segment FirstSeg = iList[0];
        Segment LastSeg = iList[numSegments - 1];

        var pLine = LineBuilderEx.CreateLineSegment(FirstSeg.StartCoordinate, LastSeg.EndCoordinate);

        if (pLine.Length * MapSRMetersPerUnit > 0.0014)
        {
          COGODirectionDistanceRadiusArcLength[0] =
              PolarRadiansToNorthAzimuthDecimalDegrees(pLine.Angle - DirectionOffset * Math.PI / 180);

          COGODirectionDistanceRadiusArcLength[1] = pLine.Length * MapSRMetersPerUnit /
                  DatasetSRMetersPerUnit / ScaleFactor;
        }
        else
        {
          COGODirectionDistanceRadiusArcLength[0] = DBNull.Value;
          COGODirectionDistanceRadiusArcLength[1] = DBNull.Value;
        }
        //check if the last segment is a circular arc
        var pCircArcLast = LastSeg as EllipticArcSegment;
        if (pCircArcLast == null)
          return true; //we already know there is no circular arc COGO

        //check if this feature includes elliptical arcs that are not circular arcs

        //Keep a copy of the center point
        var LastCenterPoint = pCircArcLast.CenterPoint;
        COGODirectionDistanceRadiusArcLength[2] = pCircArcLast.IsCounterClockwise ?
                -pCircArcLast.SemiMajorAxis : Math.Abs(pCircArcLast.SemiMajorAxis); //radius
        double dArcLengthSUM = 0;
        //use 30 times xy tolerance for circular arc segment tangency test
        //around 3cms if using default XY Tolerance - recommended
        double dTangencyToleranceTest = MapSR.XYTolerance * 30;
        for (int i = 0; i < numSegments; i++)
        {
          pCircArc = iList[i] as EllipticArcSegment;
          if (pCircArc == null)
          {
            COGODirectionDistanceRadiusArcLength[2] = DBNull.Value; //radius
            COGODirectionDistanceRadiusArcLength[3] = DBNull.Value; //arc length
            return true;
          }
          var tolerance = LineBuilderEx.CreateLineSegment(LastCenterPoint, pCircArc.CenterPoint).Length;
          if (tolerance > dTangencyToleranceTest || !pCircArc.IsCircular)
          {
            COGODirectionDistanceRadiusArcLength[2] = DBNull.Value; //radius
            COGODirectionDistanceRadiusArcLength[3] = DBNull.Value; //arc length
            return true;
          }
          dArcLengthSUM += pCircArc.Length; //arc length sum
        }
        //now check to see if the radius and arclength survived and if so, clear the distance
        if (COGODirectionDistanceRadiusArcLength[2] != DBNull.Value)
          COGODirectionDistanceRadiusArcLength[1] = DBNull.Value;

        COGODirectionDistanceRadiusArcLength[3] = dArcLengthSUM * 
              MapSRMetersPerUnit / DatasetSRMetersPerUnit / ScaleFactor;
        COGODirectionDistanceRadiusArcLength[2] = (double)COGODirectionDistanceRadiusArcLength[2] * 
              MapSRMetersPerUnit / DatasetSRMetersPerUnit / ScaleFactor;

        return true;
      }
      catch
      {
        return false;
      }
    }

    internal static double AngleDifferenceBetweenDirections(double DirectionInNorthAzimuthDegrees1,
      double DirectionInNorthAzimuthDegrees2)
    {
      var t = Math.Abs(DirectionInNorthAzimuthDegrees1 - DirectionInNorthAzimuthDegrees2) % 360.0; //fMOD in C++
      var delta = 180.0 - Math.Abs(t - 180.0);
      return delta;
    }

    internal static double InverseDirectionAsNorthAzimuth(Coordinate2D FromCoordinate, Coordinate2D ToCoordinate, bool Reversed)
    {
      var DirectionInPolarRadians = LineBuilderEx.CreateLineSegment(FromCoordinate, ToCoordinate).Angle;
      if (Reversed)
        DirectionInPolarRadians += Math.PI;
      return PolarRadiansToNorthAzimuthDecimalDegrees(DirectionInPolarRadians);
    }
    internal static double InverseDistance(Coordinate2D FromCoordinate, Coordinate2D ToCoordinate)
    {
      return LineBuilderEx.CreateLineSegment(FromCoordinate, ToCoordinate).Length;
    }
    internal static double StraightLineStartPointToEndPointDistance(Polyline myPolyline)
    {
      Coordinate2D crd1 = (myPolyline as Polyline).Points[0].Coordinate2D;
      int pntCnt = (myPolyline as Polyline).PointCount;
      Coordinate2D crd2 = (myPolyline as Polyline).Points[pntCnt - 1].Coordinate2D;
      return LineBuilderEx.CreateLineSegment(crd1, crd2).Length;
    }
    private static double PolarRadiansToNorthAzimuthDecimalDegrees(double InPolarRadians)
    {
      var AngConv = DirectionUnitFormatConversion.Instance;
      var ConvDef = new ConversionDefinition()
      {
        DirectionTypeIn = ArcGIS.Core.SystemCore.DirectionType.Polar,
        DirectionUnitsIn = ArcGIS.Core.SystemCore.DirectionUnits.Radians,
        DirectionTypeOut = ArcGIS.Core.SystemCore.DirectionType.NorthAzimuth,
        DirectionUnitsOut = ArcGIS.Core.SystemCore.DirectionUnits.DecimalDegrees
      };
      return AngConv.ConvertToDouble(InPolarRadians, ConvDef);
    }
  }


  internal class ParcelUtils
  {
    internal static double ClockwiseDownStreamEdgePosition(ParcelLineInfo line)
    {
      return line.IsReversed ? line.EndPositionOnParcelEdge : line.StartPositionOnParcelEdge;
    }

    internal static double ClockwiseUpStreamEdgePosition(ParcelLineInfo line)
    {
      return line.IsReversed ? line.StartPositionOnParcelEdge : line.EndPositionOnParcelEdge;
    }
    internal static bool HasValidLicenseForParcelLayer()
    {
      var lic = ArcGIS.Core.Licensing.LicenseInformation.Level;
      if (lic < ArcGIS.Core.Licensing.LicenseLevels.Standard)
        return false;
      else
        return true;
    }

    internal static bool ParcelEdgeAnalysis(ParcelEdgeCollection parcelEdgeCollection, out bool isClosedLoop,
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
            var featAtts = myLineInfo.FeatureAttributes;
            bool hasCOGODirection = TryGetObjectFromFieldUpperLowerCase(featAtts, "Direction", out object direction);
            bool hasCOGODistance = TryGetObjectFromFieldUpperLowerCase(featAtts, "Distance", out object distance);
            bool hasCOGORadius = TryGetObjectFromFieldUpperLowerCase(featAtts, "Radius", out object radius);
            bool hasCOGOArclength = TryGetObjectFromFieldUpperLowerCase(featAtts, "ArcLength", out object arclength);
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
            bool hasRetiredByGuid = TryGetObjectFromFieldUpperLowerCase(featAtts, "RetiredByRecord", out object guid);
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
                  //this line's start matches last line's end
                  vectorChord.Add(vect);
                else
                  //add zero length vector to keep index placeholder
                  //avoids side-case of exactly overlapping lines
                  vectorChord.Add(new Coordinate3D(0.0, 0.0, 0.0));

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
                //this line's start matches previous line's end
                vectorChord.Add(vect);
              else
                //add zero length vector to keep index placeholder
                //avoids side-case of exactly overlapping lines
                vectorChord.Add(new Coordinate3D(0.0, 0.0, 0.0));

              arcLengthList.Add(null);
              radiusList.Add(null);
              isMajorList.Add(false);
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

    internal static bool TryGetObjectFromFieldUpperLowerCase(IReadOnlyDictionary<string, object> ReadOnlyDict,
      string searchString, out object obj)
    {
      string ucaseSearch = searchString.ToUpper().Trim();
      string lcaseSearch = searchString.ToLower().Trim();
      string firstUcaseSearch = searchString.Trim()[..1].ToUpper() + searchString.Trim()[1..].ToLower();
      bool found = ReadOnlyDict.TryGetValue(searchString, out obj);
      if (!found)
        found = ReadOnlyDict.TryGetValue(ucaseSearch, out obj);
      else
        return true;

      if (!found)
        found = ReadOnlyDict.TryGetValue(lcaseSearch, out obj);
      else
        return true;

      if (!found)
        found = ReadOnlyDict.TryGetValue(firstUcaseSearch, out obj);
      else
        return true;

      return found;
    }

    internal static bool IsDefaultVersionOnFeatureService(FeatureLayer featureLayer)
    {
      using (Table table = featureLayer.GetTable())
      {
        Datastore datastore = table.GetDatastore();
        Geodatabase geodatabase = datastore as Geodatabase;
        if (geodatabase.IsVersioningSupported())
        {
          using (VersionManager versionManager = geodatabase.GetVersionManager())
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
      return false;
    }

    internal static bool HasParcelSelection(ParcelLayer parcelLayer)
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
            enableSate = fLyr.SelectionCount > 0;
        }
        if (enableSate)
          break;
      }
      return enableSate;
    }

    internal static List<long> FilterLayerOIDsAndSelectionByRecord(FeatureLayer featLayer, List<long> incomingOIDs = null,
      ParcelRecord Record = null)
    {//if incomingOIDs is NULL, uses only selection
      List<long> idsOut = new();
      if (incomingOIDs == null)
        incomingOIDs = idsOut = new List<long>(featLayer.GetSelection().GetObjectIDs()); //only selected features
      else
      {
        idsOut.AddRange(incomingOIDs);
        incomingOIDs.AddRange(featLayer.GetSelection().GetObjectIDs());//combine ids and selected features
      }
      //incomingOIDs has the oids from a selection or a set of oids passed in.
      //These are not yet filtered by Record at this point.
      //if there is a record then Filter
      if (Record != null)
      {
        QueryFilter queryFilterSelectedFeatures = new();
        queryFilterSelectedFeatures.WhereClause =
          "CREATEDBYRECORD = '{" + Convert.ToString(Record.Guid) + "}'";
        var featClass = featLayer.GetFeatureClass();
        queryFilterSelectedFeatures.ObjectIDs = incomingOIDs;
        //This search is the INTERSECTION of oids that filters out the oid's that are not in the record.
        idsOut.Clear(); //rebuild the ids list based on the new filtered query.
        using (RowCursor rowCursor = featClass.Search(queryFilterSelectedFeatures, false))
        {
          while (rowCursor.MoveNext())
          {
            using (Row rowFeat = rowCursor.Current)
            {
              idsOut.Add(rowFeat.GetObjectID());
            }
          }
        }
      }
      return new List<long>(idsOut);
    }

    internal static async Task<string> GetParcelTypeNameFromFeatureLayer(ParcelLayer myParcelFabricLayer,
      FeatureLayer featLayer, GeometryType geomType)
    {
      if (featLayer == null) //nothing to do return empty string
        return String.Empty;
      IEnumerable<string> parcelTypeNames = await myParcelFabricLayer.GetParcelTypeNamesAsync();
      foreach (string parcelTypeName in parcelTypeNames)
      {
        if (geomType == GeometryType.Polygon)
        {
          var polygonLyrParcelTypeEnum = await myParcelFabricLayer.GetParcelPolygonLayerByTypeNameAsync(parcelTypeName);
          foreach (FeatureLayer lyr in polygonLyrParcelTypeEnum)
            if (lyr == featLayer)
              return parcelTypeName;

          polygonLyrParcelTypeEnum = await myParcelFabricLayer.GetHistoricParcelPolygonLayerByTypeNameAsync(parcelTypeName);
          foreach (FeatureLayer lyr in polygonLyrParcelTypeEnum)
            if (lyr == featLayer)
              return parcelTypeName;
        }
        if (geomType == GeometryType.Polyline)
        {
          var lineLyrParcelTypeEnum = await myParcelFabricLayer.GetParcelLineLayerByTypeNameAsync(parcelTypeName);
          foreach (FeatureLayer lyr in lineLyrParcelTypeEnum)
            if (lyr == featLayer)
              return parcelTypeName;

          lineLyrParcelTypeEnum = await myParcelFabricLayer.GetHistoricParcelLineLayerByTypeNameAsync(parcelTypeName);
          foreach (FeatureLayer lyr in lineLyrParcelTypeEnum)
            if (lyr == featLayer)
              return parcelTypeName;
        }
      }
      return string.Empty;
    }

    internal static async Task<FeatureLayer> GetFirstFeatureLayerFromParcelTypeName(ParcelLayer myParcelFabricLayer,
      string ParcelTypeName, GeometryType geomType)
    {
      if (geomType == GeometryType.Polygon)
      {
        var targetPolygonTypeEnum =
          await myParcelFabricLayer.GetParcelPolygonLayerByTypeNameAsync(ParcelTypeName);
        if (targetPolygonTypeEnum != null)
          return targetPolygonTypeEnum.FirstOrDefault();
      }
      else
      {
        var targetPolylineTypeEnum =
          await myParcelFabricLayer.GetParcelLineLayerByTypeNameAsync(ParcelTypeName);
        if (targetPolylineTypeEnum != null)
          return targetPolylineTypeEnum.FirstOrDefault();
      }
      return null;
    }

    internal static void GetParcelPolygonFeatureLayersSelection(ParcelLayer myParcelFabricLayer,
      out Dictionary<FeatureLayer, List<long>> ParcelPolygonSelections)
    {
      List<FeatureLayer> featureLayer = new();
      ParcelPolygonSelections = new();
      var parcelTypes = myParcelFabricLayer.GetParcelTypeNamesAsync().Result;

      foreach (var parcelType in parcelTypes)
      {
        var fLyrList = myParcelFabricLayer.GetParcelPolygonLayerByTypeNameAsync(parcelType).Result;
        if (fLyrList == null) continue;
        foreach (var fLyr in fLyrList)
        {
          if (fLyr.SelectionCount > 0)
            featureLayer.Add(fLyr);
        }
      }

      foreach (var lyr in featureLayer)
      {
        var fc = lyr.GetFeatureClass();
        List<long> lstOids = new();
        using (RowCursor rowCursor = lyr.GetSelection().Search())
        {
          while (rowCursor.MoveNext())
          {
            using (Row rowFeat = rowCursor.Current)
            {
              if (!ParcelPolygonSelections.ContainsKey(lyr))
                ParcelPolygonSelections.Add(lyr, lstOids);
              lstOids.Add(rowFeat.GetObjectID());
            }
          }
        }
        if (lstOids.Count > 0)
          ParcelPolygonSelections[lyr] = lstOids;
      }
    }

    internal static bool GetTargetFolder(string ConfigurationSettingsName, out string folderPath)
    {
      folderPath = ConfigurationsLastUsed.Default[ConfigurationSettingsName] as string;
      BrowseProjectFilter bf = new(ItemFilters.Folders);
      //Display the filter in an Open Item dialog
      OpenItemDialog aBrowseForFolder = new()
      {
        Title = "Select A Target Folder",
        InitialLocation = folderPath,
        MultiSelect = false,
        BrowseFilter = bf
      };
      bool? ok = aBrowseForFolder.ShowDialog();
      if (ok == true)
      {
        var myItems = aBrowseForFolder.Items;
        folderPath = myItems.First().Path;

        //sTextFileFabricPointIDToGuid = Path.Combine(folderPath, Module1.PointIDMapTextFile);
        ConfigurationsLastUsed.Default[ConfigurationSettingsName] = folderPath;
        ConfigurationsLastUsed.Default.Save();

        return true;
      }
      else
        return false;
    }
  }
}

