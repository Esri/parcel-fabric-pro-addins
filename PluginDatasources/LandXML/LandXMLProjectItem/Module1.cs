using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.IO;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.PluginDatastore;
using ArcGIS.Core.Data.UtilityNetwork.Trace;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.KnowledgeGraph;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Internal.Layouts.Utilities;

namespace LandXMLProjectItem
{
    internal class Module1 : Module
    {
        private static Module1 _this = null;

        /// <summary>
        /// Retrieve the singleton instance to this module here
        /// </summary>
        public static Module1 Current => _this ??= (Module1)FrameworkApplication.FindModule("LandXMLProjectItem_Module");

    internal static string AddLandXMLGroupLayer(GroupLayer groupLayer, PluginDatastore pluginLandXML)
    {
      List<FeatureLayer> lstTemplateLayers = new ();
      List<Operation> lstCreateLayerOperations = new();
      List<Operation> lstLeaveNoTraceOperations = new ();
      var opManager = MapView.Active?.Map?.OperationManager;
      QueuedTask.Run(() =>
      {
        using (pluginLandXML)
          foreach (var tableName in pluginLandXML.GetTableNames())
            using (var tbl = pluginLandXML.OpenTable(tableName))
              if (tbl is FeatureClass fc)
              {
                string strTypeName = "";

                if (fc.GetName().Contains("POINTS", StringComparison.CurrentCultureIgnoreCase))
                  strTypeName = "POINTS";
                else if (fc.GetName().Contains("LINES", StringComparison.CurrentCultureIgnoreCase))
                  strTypeName = "LINES";
                else if (fc.GetName().Contains("PARCELS", StringComparison.CurrentCultureIgnoreCase))
                  strTypeName = "PARCELS";
                else if (fc.GetName().Contains("PLANS", StringComparison.CurrentCultureIgnoreCase))
                  strTypeName = "PLANS";

                string layerTemplateFileName = @"LandXMLLayerTemplate_" + strTypeName + ".lyrx";
                string urlLyrTemplate = Path.Combine(Module1.AssemblyDirectory, 
                  "LayerTemplateFiles", layerTemplateFileName);

                var lyrTemplateURI = new Uri(urlLyrTemplate);

                var lyrTemplate = LayerFactory.Instance.CreateLayer(lyrTemplateURI,
                  groupLayer) as FeatureLayer;
                AddLastOperationToList(opManager, ref lstLeaveNoTraceOperations, "Mapping"); //undo create layer

                var rendererFromTemplate = lyrTemplate.GetRenderer();

                lstTemplateLayers.Add(lyrTemplate);

                var lyrCreateParams = new FeatureLayerCreationParams(fc);
                FeatureLayer flyr = LayerFactory.Instance.CreateLayer<FeatureLayer>(lyrCreateParams, groupLayer);
                lstCreateLayerOperations.Add(opManager.PeekUndo());

                flyr.SetRenderer(rendererFromTemplate);
                AddLastOperationToList(opManager, ref lstLeaveNoTraceOperations, "Mapping"); //undo renderer update

              }

        foreach (var templateFlyr in lstTemplateLayers)
        {
          groupLayer.RemoveLayer(templateFlyr);
          AddLastOperationToList(opManager, ref lstLeaveNoTraceOperations, "Mapping");
        }

        groupLayer.SetExpanded(true);
        AddLastOperationToList(opManager, ref lstLeaveNoTraceOperations, "Mapping");
        foreach (var op in lstLeaveNoTraceOperations)
          opManager.RemoveUndoOperation(op);


        //TODO: Can we combine the undos for create layer?
        //opManager.CreateCompositeOperation()

        //// find all the undo operations for the Mapping category
        //var ops = opManager.FindUndoOperations(o => o.Category == "Mapping");
      });
      return "";
    }

    static internal void AddLastOperationToList(OperationManager opManager, ref List<Operation> Operations, string Category = "")
    {
      var op = opManager?.PeekUndo(Category);
      if (op != null)
        Operations.Add(op);
    }
    static internal string AssemblyDirectory
    {
      get
      {
        string codeBase = System.Reflection.Assembly.GetExecutingAssembly().Location;
        UriBuilder uri = new(codeBase);
        string path = Uri.UnescapeDataString(uri.Path);
        return Path.GetDirectoryName(path);
      }
    }

    #region Overrides
    /// <summary>
    /// Called by Framework when ArcGIS Pro is closing
    /// </summary>
    /// <returns>False to prevent Pro from closing, otherwise True</returns>
    protected override bool CanUnload()
        {
            //TODO - add your business logic
            //return false to ~cancel~ Application close
            return true;
        }

        #endregion Overrides

    }
}
