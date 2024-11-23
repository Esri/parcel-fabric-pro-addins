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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Controls;
using ArcGIS.Desktop.Core.UnitFormats;

namespace ParcelsAddin
{
  internal class ConfigureUpdateCOGOViewModel : PropertyChangedBase
  {
    private ConfigureUpdateCOGOModel _ConfigureUpdateCOGOModel;
    private readonly string _spatialReferenceSource;

    private readonly string _distanceUnitAbbr = "m";
    
    private readonly bool _updateDistances = true;
    private readonly bool[] _updateDistancesOption = new bool[] { true, false, false };
    private readonly string _distanceDifferenceToleranceInBackstageUnits;
    private readonly string _distanceDifferenceToleranceInMeters;
    private readonly double _distanceDifferenceToleranceInMetersNumeric;

    private readonly bool _updateDirections = true;
    private readonly bool[] _updateDirectionsOption = new bool[] { true, false, false };
    private readonly string _directionDifferenceToleranceInSeconds;
    private readonly double _directionDifferenceToleranceInDecDeg;
    private readonly string _lateralOffsetToleranceInBackstageUnits;
    private readonly string _lateralOffsetToleranceInMeters;
    private readonly double _lateralOffsetToleranceInMetersNumeric;

    public static ICommand OKCommand
    {
      get
      {
        return new RelayCommand((dlgParam) =>
        {
          ProWindow param = dlgParam as ProWindow;
          param.DialogResult = true;
        }, () => true);
      }
    }

    public ConfigureUpdateCOGOViewModel(double metersPerBackstageDistanceUnit, 
      int DistanceDisplayPrecision = 3, string DistanceUnitAbbreviation = "m")
    {
      _spatialReferenceSource = "Active map";

      _distanceUnitAbbr = DistanceUnitAbbreviation;

      _updateDistances = true;
      _updateDistancesOption[0] = true;
      _distanceDifferenceToleranceInBackstageUnits = "0.01";
      _distanceDifferenceToleranceInMeters = "0.01";
      
      _updateDirections = true;
      _updateDirectionsOption[0] = true;
      _directionDifferenceToleranceInSeconds = "10";
      _lateralOffsetToleranceInBackstageUnits = "0.01";
      _lateralOffsetToleranceInMeters = "0.01";

      string sParamString = ConfigurationsLastUsed.Default["ConfigureUpdateCOGOLastUsedParams"] as string;
      string[] sParams = sParamString.Split('|');
      if (sParams.Length == 0)
      {
        _spatialReferenceSource = "Active map";
        _distanceUnitAbbr = DistanceUnitAbbreviation;

        _updateDistances = true;
        _updateDistancesOption[0] = true;
        _distanceDifferenceToleranceInBackstageUnits = "0.01";
        _distanceDifferenceToleranceInMeters = "0.01";

        _updateDirections = true;
        _updateDirectionsOption[0] = true;
        _directionDifferenceToleranceInSeconds = "10";
        _lateralOffsetToleranceInBackstageUnits = "0.01";
        _lateralOffsetToleranceInMeters = "0.01";
      }
      else
      {
        try
        {
          _spatialReferenceSource = sParams[0];
          if (String.IsNullOrEmpty(_spatialReferenceSource))
            _spatialReferenceSource = "Active map";
        }
        catch { _spatialReferenceSource = "Active map"; }

        try
        {
          _updateDistances = sParams[1] == Boolean.TrueString;
        }
        catch { _updateDistances = true; }

        try
        {
          _updateDistancesOption[0] = sParams[2] == Boolean.TrueString;
        }
        catch { _updateDistancesOption[0] = true; }

        try
        {
          _updateDistancesOption[1] = sParams[3] == Boolean.TrueString;
        }
        catch { _updateDistancesOption[1] = false; }

        try
        {
          _updateDistancesOption[2] = sParams[4] == Boolean.TrueString;
        }
        catch { _updateDistancesOption[2] = false; }

        try //always stored in metric
        {
          _distanceDifferenceToleranceInMeters = sParams[5];
          if (String.IsNullOrEmpty(_distanceDifferenceToleranceInMeters))
            _distanceDifferenceToleranceInMeters = "0.01";
        }
        catch { _distanceDifferenceToleranceInMeters = "0.01"; }

        try
        {
          _updateDirections = sParams[6] == Boolean.TrueString;
        }
        catch { _updateDirections = true; }

        try
        {
          _updateDirectionsOption[0] = sParams[7] == Boolean.TrueString;
        }
        catch { _updateDirectionsOption[0] = true; }

        try
        {
          _updateDirectionsOption[1] = sParams[8] == Boolean.TrueString;
        }
        catch { _updateDirectionsOption[1] = false; }

        try
        {
          _updateDirectionsOption[2] = sParams[9] == Boolean.TrueString;
        }
        catch { _updateDirectionsOption[2] = false; }

        try
        {
          _directionDifferenceToleranceInSeconds = sParams[10];
          if (String.IsNullOrEmpty(_directionDifferenceToleranceInSeconds))
            _directionDifferenceToleranceInSeconds = "10";
        }
        catch { _directionDifferenceToleranceInSeconds = "10"; }

        try
        {
          _lateralOffsetToleranceInMeters = sParams[11];
          if (String.IsNullOrEmpty(_lateralOffsetToleranceInMeters))
            _lateralOffsetToleranceInMeters = "0.01";
        }
        catch { _lateralOffsetToleranceInMeters = "0.01"; }


        //Convert units for UI 
        if (!Double.TryParse(_distanceDifferenceToleranceInMeters, out _distanceDifferenceToleranceInMetersNumeric))
          _distanceDifferenceToleranceInMetersNumeric = 0.01;

        if (!Double.TryParse(_lateralOffsetToleranceInMeters, out _lateralOffsetToleranceInMetersNumeric))
          _lateralOffsetToleranceInMetersNumeric = 0.01;

        if (!Double.TryParse(_directionDifferenceToleranceInSeconds, out _directionDifferenceToleranceInDecDeg))
          _directionDifferenceToleranceInDecDeg = 10.0 / 3600.0;
        else
          _directionDifferenceToleranceInDecDeg /= 3600.0;

        string sFormat = new('0', DistanceDisplayPrecision);
        sFormat = "0." + sFormat;
        _distanceDifferenceToleranceInBackstageUnits =
          (_distanceDifferenceToleranceInMetersNumeric / metersPerBackstageDistanceUnit).ToString(sFormat);
        _lateralOffsetToleranceInBackstageUnits =
          (_lateralOffsetToleranceInMetersNumeric / metersPerBackstageDistanceUnit).ToString(sFormat);


      }
      _ConfigureUpdateCOGOModel = new ConfigureUpdateCOGOModel
      {
        SpatialReferenceSource = _spatialReferenceSource,
        DistanceUnitAbbreviation = _distanceUnitAbbr,
        UpdateDistances = _updateDistances,
        UpdateDistancesOption = _updateDistancesOption,
        DistanceDifferenceToleranceInBackstageUnits = _distanceDifferenceToleranceInBackstageUnits,
        DistanceDifferenceToleranceInMeters = _distanceDifferenceToleranceInMetersNumeric,
        UpdateDirections = _updateDirections,
        UpdateDirectionsOption = _updateDirectionsOption,
        DifferenceDirectionToleranceSeconds = _directionDifferenceToleranceInSeconds,
        DifferenceDirectionToleranceDecimalDegrees= _directionDifferenceToleranceInDecDeg,
        LateralOffsetToleranceInBackstageUnits = _lateralOffsetToleranceInBackstageUnits,
        LateralOffsetToleranceInMeters = _lateralOffsetToleranceInMetersNumeric
      };

    }
    public ConfigureUpdateCOGOModel ConfigureUpdateCOGOModel
    {
      get { return _ConfigureUpdateCOGOModel; }
      set { _ConfigureUpdateCOGOModel = value; }
    }

  }
}

