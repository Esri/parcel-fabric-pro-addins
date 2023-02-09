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
  internal class ConfigureAreaUnits : Button
  {
    private ConfigureAreaUnitsViewModel _VM = new ConfigureAreaUnitsViewModel();
    protected override void OnClick()
    {
      #region Collect parameters from dialog and save to settings
      var ConfigureAreaUnitsInput = new ConfigureAreaUnitsDialog();
      ConfigureAreaUnitsInput.Owner = FrameworkApplication.Current.MainWindow;
      ConfigureAreaUnitsInput.DataContext = _VM;
      
      if (ConfigureAreaUnitsInput.ShowDialog() == true)
      {
        long largeParcelAreaUnitCode = _VM.ConfigureAreaUnitsModel.AreaUnitsCodeLookup[_VM.ConfigureAreaUnitsModel.AreaUnitName];
        _VM.ConfigureAreaUnitsModel.LargeParcelAreaUnitCode = largeParcelAreaUnitCode;
        ConfigureAreaUnitsDlg.Default["LastUsedParams"] = _VM.ConfigureAreaUnitsModel.AreaUnitName + "|" +
          _VM.ConfigureAreaUnitsModel.LargeAreaValueText + "|" + largeParcelAreaUnitCode.ToString()
          + "|" + _VM.ConfigureAreaUnitsModel.LargeParcelAreaInSquareMeters.ToString()
          + "|" + _VM.ConfigureAreaUnitsModel.SquareMetersPerAreaUnit.ToString();
        ConfigureAreaUnitsDlg.Default.Save();//comment out if you only want to save settings within each app session
      }
      #endregion Collect parameters from dialog and save to settings
    }
  }

}
