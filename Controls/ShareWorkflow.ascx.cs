﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

using Rock;
using Rock.Data;
using Rock.Model;
using Rock.Web.UI;

using EntityCoding;
using EntityCoding.Exporters;

//
// TODO: This block should be prettied up with tab buttons or something to
// visually switch between export mode and import mode rather than mixing
// both in one panel.
//
// TODO: Import status text should be updated to be a bit prettier. Using
// a pre tag was a quick and dirty way to go. It would be nice if the
// Created/Found Existing messages also included the friendly name of the
// entity instead of the Guid (like Preview does).
//
// BUGFIX: Currently, the IsSystem field is exported and imported. This
// should probably be skipped. Example case, the `Global` category for
// DefinedType does not have a common Guid across installs. So if a DefinedType
// is exported and imported you end up with a duplicate `Global` category.
// That is manageable, but that duplicate `Global` category also is set to IsSystem=1
// which prevents the user from cleaning up without editing SQL.
//
namespace RockWeb.Blocks.Utility
{
    /// <summary>
    /// Export and import workflows from Rock.
    /// </summary>
    /// <seealso cref="Rock.Web.UI.RockBlock" />
    [DisplayName( "Share Workflow" )]
    [Category( "Utility" )]
    [Description( "Export and import workflows from Rock." )]
    public partial class ShareWorkflow : RockBlock
    {
        #region Base Method Overrides

        /// <summary>
        /// Initialize basic information about the page structure and setup the default content.
        /// </summary>
        /// <param name="sender">Object that is generating this event.</param>
        /// <param name="e">Arguments that describe this event.</param>
        protected void Page_Load( object sender, EventArgs e )
        {
            ScriptManager.GetCurrent( this.Page ).RegisterPostBackControl( btnExport );
        }

        #endregion

        #region Core Methods

        /// <summary>
        /// Binds the preview grid.
        /// </summary>
        protected void BindPreviewGrid()
        {
            List<PreviewEntity> previewEntities = ( List<PreviewEntity> ) ViewState["PreviewEntities"];

            if ( previewEntities == null )
            {
                previewEntities = new List<PreviewEntity>();
            }

            var query = previewEntities.AsQueryable();

            if ( gPreview.SortProperty != null )
            {
                query = query.Sort( gPreview.SortProperty );
            }

            gPreview.DataSource = query;
            gPreview.DataBind();
        }

        /// <summary>
        /// Get a friendly name for the entity, optionally including the short name for the
        /// entity type. This attempts a ToString() on the entity and if that returns what
        /// appears to be a valid name (no &lt; character and less than 40 characters) then
        /// it is used as the name. Otherwise the Guid is used for the name.
        /// </summary>
        /// <param name="entity">The entity whose name we wish to retrieve.</param>
        /// <returns>A string that can be displayed to the user to identify this entity.</returns>
        static protected string EntityFriendlyName( IEntity entity )
        {
            string name;

            name = entity.ToString();
            if ( name.Length > 40 || name.Contains( "<" ) )
            {
                name = entity.Guid.ToString();
            }

            return name;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles the Click event of the btnPreview control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void btnPreview_Click( object sender, EventArgs e )
        {
            RockContext rockContext = new RockContext();
            var workflowTypeService = new WorkflowTypeService( rockContext );
            var workflowType = workflowTypeService.Get( wtpExport.SelectedValueAsId().Value );
            var coder = new EntityCoder( new RockContext() );
            var exporter = new WorkflowTypeExporter();
            coder.EnqueueEntity( workflowType, exporter );

            List<PreviewEntity> previewEntities = new List<PreviewEntity>();

            foreach ( var qe in coder.Entities )
            {
                string shortType = CodingHelper.GetEntityType( qe.Entity ).Name;

                if ( shortType == "Attribute" || shortType == "AttributeValue" || shortType == "AttributeQualifier" || shortType == "WorkflowActionFormAttribute" )
                {
                    continue;
                }

                var preview = new PreviewEntity
                {
                    Guid = qe.Entity.Guid,
                    Name = EntityFriendlyName( qe.Entity ),
                    ShortType = shortType,
                    IsCritical = qe.IsCritical,
                    IsNewGuid = qe.RequiresNewGuid,
                    Paths = qe.ReferencePaths.Select( p => p.ToString() ).ToList()
                };

                previewEntities.Add( preview );
            }

            ViewState["PreviewEntities"] = previewEntities;

            BindPreviewGrid();
        }

        /// <summary>
        /// Handles the Click event of the btnExport control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void btnExport_Click( object sender, EventArgs e )
        {
            RockContext rockContext = new RockContext();
            var workflowTypeService = new WorkflowTypeService( rockContext );
            var workflowType = workflowTypeService.Get( wtpExport.SelectedValueAsId().Value );
            var coder = new EntityCoder( new RockContext() );
            coder.EnqueueEntity( workflowType, new WorkflowTypeExporter() );

            var container = coder.GetExportedEntities();

            // TODO: This should probably be stored as a BinaryFile and the user given a link to
            // click to download.
            Page.EnableViewState = false;
            Page.Response.Clear();
            Page.Response.ContentType = "application/json";
            Page.Response.AppendHeader( "Content-Disposition", string.Format( "attachment; filename=\"{0}_{1}.json\"", workflowType.Name.MakeValidFileName(), RockDateTime.Now.ToString( "yyyyMMddHHmm" ) ) );
            Page.Response.Write( Newtonsoft.Json.JsonConvert.SerializeObject( container, Newtonsoft.Json.Formatting.Indented ) );
            Page.Response.Flush();
            Page.Response.End();
        }

        /// <summary>
        /// Handles the Click event of the lbImport control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void lbImport_Click( object sender, EventArgs e )
        {
            if ( !fuImport.BinaryFileId.HasValue || !cpImportCategory.SelectedValueAsId().HasValue )
            {
                return;
            }

            using ( var rockContext = new RockContext() )
            {
                var binaryFileService = new BinaryFileService( rockContext );
                var binaryFile = binaryFileService.Get( fuImport.BinaryFileId ?? 0 );
                var categoryService = new CategoryService( rockContext );

                var container = Newtonsoft.Json.JsonConvert.DeserializeObject<ExportedEntitiesContainer>( binaryFile.ContentsToString() );
                List<string> messages;

                var decoder = new EntityDecoder( new RockContext() );
                decoder.UserValues.Add( "WorkflowCategory", categoryService.Get( cpImportCategory.SelectedValueAsId().Value ) );

                var success = decoder.Import( container, cbDryRun.Checked, out messages );

                ltImportResults.Text = string.Empty;
                foreach ( var msg in messages )
                {
                    ltImportResults.Text += string.Format( "{0}\n", msg.EncodeHtml() );
                }

                pnlImportResults.Visible = true;

                if ( success )
                {
                    fuImport.BinaryFileId = null;
                }
            }
        }

        /// <summary>
        /// Handles the GridRebind event of the gPreview control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="Rock.Web.UI.Controls.GridRebindEventArgs"/> instance containing the event data.</param>
        protected void gPreview_GridRebind( object sender, Rock.Web.UI.Controls.GridRebindEventArgs e )
        {
            BindPreviewGrid();
        }

        #endregion

        [Serializable]
        protected class PreviewEntity
        {
            public Guid Guid { get; set; }

            public string Name { get; set; }

            public string ShortType { get; set; }

            public bool IsCritical { get; set; }

            public bool IsNewGuid { get; set; }

            public List<string> Paths { get; set; }
        }
    }
}
