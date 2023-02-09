/*   Copyright 2023 Esri
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
    private readonly ParcelUtils _parcelUtils = new();
    protected override void OnUpdate()
    {
      QueuedTask.Run( () =>
      {
        var myParcelFabricLayer =
        MapView.Active?.Map?.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();

        //if there is no fabric in the map then bail
        if (myParcelFabricLayer == null)
        {
          this.Enabled = false;
          this.DisabledTooltip = "There is no fabric in the map.";
          return;
        }
        if (_parcelUtils.HasParcelSelection(myParcelFabricLayer))
        {
          this.Enabled = true;  //tool is enabled  
                                //this.Tooltip = "";
        }
        else
        {
          this.Enabled = false;  //tool is disabled  
                                  //customize your disabledText here
          this.DisabledTooltip = "There is no parcel selection.";
        }
      });
    }
    protected override async void OnClick()
    {
      //first confirm we have a license...
      var lic = ArcGIS.Core.Licensing.LicenseInformation.Level;
      if (lic < ArcGIS.Core.Licensing.LicenseLevels.Standard)
      {
        MessageBox.Show("Insufficient License Level.");
        return;
      }
      var cgUtils = new COGOUtils();

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
        if (_parcelUtils.IsDefaultVersionOnFeatureService(recordsLyr))
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
          var typeNamesEnum = await myParcelFabricLayer.GetParcelPolygonLayerByTypeNameAsync(sTargetParcelType);
          if (typeNamesEnum.Count() == 0)
            return "Target parcel type " + sTargetParcelType + " not found. Please try again.";
          var featTargetLyr = typeNamesEnum.FirstOrDefault();

          List<long> ids = new List<long>((featSrcLyr as FeatureLayer).GetSelection().GetObjectIDs());
          var myKVP2 = new KeyValuePair<MapMember, List<long>>(featSrcLyr, ids);
          var sourceParcels = new List<KeyValuePair<MapMember, List<long>>> { myKVP2 };

          //Build the Clip geometry and also Confirm all selected parcels belong to the active record
          var enumSel = featSrcLyr.GetSelection().GetObjectIDs().GetEnumerator();
          List<Polygon> polys = new List<Polygon>();
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
          if (sid1.Trim() == String.Empty)
          {
            string sMsg = "Selected parcel(s) do not have a record.";
            return sMsg;
          }
          Guid guid = new Guid(sid1);
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
            tol = Math.Atan(tol / (6378100 / _metersPerUnit));

          var SearchGeometry = GeometryEngine.Instance.Buffer(ClipGeometry, -tol);//this is used for the search only

          //get the intersecting non-historic parcels of the target type that are in a different record
          SpatialQueryFilter pSpatQu = new SpatialQueryFilter();
          pSpatQu.FilterGeometry = SearchGeometry;
          pSpatQu.SpatialRelationship = SpatialRelationship.Intersects;
          pSpatQu.WhereClause = "CREATEDBYRECORD <> '" + sid1 + "'";
          List<long> idTargetParcel = new List<long>();
          using (RowCursor rowCursor = featTargetLyr.Search(pSpatQu))
          {
            while (rowCursor.MoveNext())
            {
              using (Row rowFeat = rowCursor.Current)
                idTargetParcel.Add(rowFeat.GetObjectID());
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
          if (ex.Message.Trim() == String.Empty)
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
  }
}
