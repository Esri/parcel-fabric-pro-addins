/* Copyright 2024 Esri
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
using System.ComponentModel;
using ArcGIS.Core.SystemCore;

namespace ParcelsAddin
{
  public class ConfigureParcelReportModel : INotifyPropertyChanged
  {
    #region INotifyPropertyChanged Members  

    public event PropertyChangedEventHandler PropertyChanged;
    private void NotifyPropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
      {
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
      }
    }
    #endregion

    private string _directionTypeName;
    private List<string> _directionTypesNameList;
    private List<DirectionType> _directionTypesList;
    private Dictionary<string, DirectionType> _dictDirectionTypeLookup;
    private DirectionType _directionType;

    private string _distanceUnitName;
    private List<string> _distanceUnitsNameList;
    private double _metersPerLinearUnit;
    private int _distanceUnitPrecision;
    
    private string _circularArcDirectionParameter;
    private List<string> _circularArcDirectionParameterList;

    private string _directionSymbol;
    private List<string> _directionSymbolList;

    private string _textFormatStyle;
    private List<string> _textFormatStyleList;

    #region Directions
    public string DirectionTypeName
    {
      get
      {
        return _directionTypeName;
      }
      set
      {
        _directionTypeName = value;
        NotifyPropertyChanged(nameof(DirectionTypeName));
      } //use nameof to avoid hard coding strings.
    }

    public DirectionType DirectionType
    {
      get
      {
        return _directionType;
      }
      set
      {
        _directionType = value;
        NotifyPropertyChanged(nameof(DirectionType));
      } //use nameof to avoid hard coding strings.
    }
    public List<DirectionType> DirectionTypesList
    {
      get
      {
        return _directionTypesList;
      }
      set
      {
        _directionTypesList = value;
        NotifyPropertyChanged(nameof(DirectionTypesList));
      }
    }

    public Dictionary<string, DirectionType> DirectionTypeLookup
    {
      get
      {
        return _dictDirectionTypeLookup;
      }
      set
      {
        _dictDirectionTypeLookup = value;
        NotifyPropertyChanged(nameof(DirectionTypeLookup));
      }
    }
    public List<string> DirectionTypesNameList
    {
      get
      {
        return _directionTypesNameList;
      }
      set
      {
        _directionTypesNameList = value;
        NotifyPropertyChanged(nameof(DirectionTypesNameList));
      }
    }
    #endregion

    #region Distances
    public string DistanceUnitName
    {
      get
      {
        return _distanceUnitName;
      }
      set
      {
        _distanceUnitName = value;

        //set the conversion factor
        if (_distanceUnitName.ToLower() == "meters")
        {
          _metersPerLinearUnit = 1.0;
          _distanceUnitPrecision = 3;
        }
        else if (_distanceUnitName.ToLower() == "feet")
        {
          _metersPerLinearUnit = 0.3048;
          _distanceUnitPrecision = 2;
        }
        else if (_distanceUnitName.ToLower() == "us feet")
        {
          _metersPerLinearUnit = 0.30480060960121924;
          _distanceUnitPrecision = 2;
        }
        else if (_distanceUnitName.ToLower() == "chains")
        {
          _metersPerLinearUnit = 20.1168;
          _distanceUnitPrecision = 4;
        }
        else
        {
          _metersPerLinearUnit = 1.0;
          _distanceUnitPrecision = 3;
        }

        NotifyPropertyChanged(nameof(DistanceUnitName));
      } //use nameof to avoid hard coding strings.
    }
    public List<string> DistanceUnitsNameList
    {
      get
      {
        return _distanceUnitsNameList;
      }
      set
      {
        _distanceUnitsNameList = value;
        NotifyPropertyChanged(nameof(DistanceUnitsNameList));
      }
    }
    public double MetersPerLinearUnit
    {
      get
      {
        return _metersPerLinearUnit;
      }
    }
    public int DistanceUnitPrecision
    {
      get
      {
        return _distanceUnitPrecision;
      }
    }
    #endregion

    #region Circular Arc Parameter
    public string CircularArcDirectionParameter
    {
      get
      {
        return _circularArcDirectionParameter;
      }
      set
      {
        _circularArcDirectionParameter = value;
        NotifyPropertyChanged(nameof(CircularArcDirectionParameter));
      }
    }
    public List<string> CircularArcDirectionParameterList
    {
      get
      {
        return _circularArcDirectionParameterList;
      }
      set
      {
        _circularArcDirectionParameterList = value;
        NotifyPropertyChanged(nameof(CircularArcDirectionParameterList));
      }
    }
    #endregion

    #region Direction Symbol
    public string DirectionSymbol
    {
      get
      {
        return _directionSymbol;
      }
      set
      {
        _directionSymbol = value;
        NotifyPropertyChanged(nameof(DirectionSymbol));
      }
    }

    public List<string> DirectionSymbolList
    {
      get
      {
        return _directionSymbolList;
      }
      set
      {
        _directionSymbolList = value;
        NotifyPropertyChanged(nameof(DirectionSymbolList));
      }
    }
    #endregion

    #region Text Format Style
    public string TextFormatStyle
    {
      get
      {
        return _textFormatStyle;
      }
      set
      {
        _textFormatStyle = value;
        NotifyPropertyChanged(nameof(TextFormatStyle));
      }
    }
    public List<string> TextFormatStyleList
    {
      get
      {
        return _textFormatStyleList;
      }
      set
      {
        _textFormatStyleList = value;
        NotifyPropertyChanged(nameof(TextFormatStyleList));
      }
    }

    #endregion

  }
}
