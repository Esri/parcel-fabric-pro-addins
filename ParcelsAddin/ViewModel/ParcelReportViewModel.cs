﻿/*   Copyright 2023 Esri
   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

   See the License for the specific language governing permissions and
   limitations under the License.
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
namespace ParcelsAddin
{
  internal class ParcelReportViewModel : PropertyChangedBase
  {
    private ParcelReport _ParcelReport;
    private string _parcelReportText;
    public ICommand OKCommand
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

    public ParcelReportViewModel()
    {
      _parcelReportText = "";

      _ParcelReport = new ParcelReport
      {
        ParcelReportText = _parcelReportText,
      };

    }
    public ParcelReport ParcelReport
    {
      get { return _ParcelReport; }
      set { _ParcelReport = value; }
    }
  }
}
