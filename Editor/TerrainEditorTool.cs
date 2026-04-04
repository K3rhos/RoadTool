using Editor;
using System.Linq;
using Sandbox;
using RedSnail.RoadTool;

namespace RedSnail.RoadTool.Editor;

/// <summary>
/// Deformation of the terrain
/// </summary>
[Title( "Terrain" )]
[Icon( "landscape" )]
[Group( "1" )]
[Order( 0 )]
public class TerrainEditorTool : EditorTool
{
    public override Widget CreateToolSidebar()
    {
        ToolSidebarWidget sidebar = new ToolSidebarWidget();
        sidebar.AddTitle( "Terrain", "landscape" );

        var road = SceneEditorSession.Active.Selection.OfType<RoadComponent>().FirstOrDefault();
        if ( road == null )
        {
            var go = SceneEditorSession.Active.Selection.OfType<GameObject>().FirstOrDefault();
            road = go?.Components.Get<RoadComponent>();
        }

        Layout group = sidebar.AddGroup( "Properties" );

        if ( road.IsValid() )
        {
            // On crée une feuille de contrôle liée aux propriétés du composant RoadComponent
            var serialized = road.GetSerialized();
            var sheet = new ControlSheet();
            sheet.AddRow( serialized.GetProperty( "TerrainFalloffRadius" ) );
            sheet.AddRow( serialized.GetProperty( "TerrainStepPrecision" ) );
            sheet.AddRow( serialized.GetProperty( "TerrainHeightOffset" ) );

            group.Add( sheet );
        }
        else
        {
            group.Add( new Label( "Select a route to edit" ) );
        }

		sidebar.Layout.Add( BuildControlButtons() );
		sidebar.Layout.AddStretchCell();

        return sidebar;
    }

    private Layout BuildControlButtons()
    {
        var row = Layout.Row();
        row.Spacing = 4;
        row.Margin = 4;
        row.Add( new Button( "Apply to the Ground", "landscape" ) { Clicked = AlignTerrainToRoad } );
        return row;
    }


    /// <summary>
    /// This method applies to the selected RoadComponent.
    /// </summary>
    public static void AlignTerrainToRoad()
    {
        RoadComponent road = SceneEditorSession.Active.Selection.OfType<RoadComponent>().FirstOrDefault();

        if ( road == null )
        {
            var go = SceneEditorSession.Active.Selection.OfType<GameObject>().FirstOrDefault();
            road = go?.Components.Get<RoadComponent>();
        }

        if ( road == null )
        {
            Log.Warning( "RoadTool: Please select a RoadComponent in the hierarchy to use this tool.." );
            return;
        }

        road.AdaptTerrainToRoad();
    }
}
