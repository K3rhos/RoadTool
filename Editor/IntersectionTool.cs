using Sandbox;
using Editor;

namespace RedSnail.RoadTool.Editor;

/// <summary>
/// Create and manage road and road intersection.
/// </summary>
[Title( "Create Road/Intersection" )]
[Icon( "roundabout_left" )]
[Alias( "intersection" )]
[Group( "1" )]
[Order( 0 )]
public class IntersectionTool : EditorTool
{
	public override void OnEnabled()
	{

	}

	public override Widget CreateToolSidebar()
	{
		ToolSidebarWidget sidebar = new ToolSidebarWidget();
		sidebar.AddTitle( "Intersection", "roundabout_left" );

		Layout group = sidebar.AddGroup( "Create" );
		Layout row = Layout.Row();

		IconButton road = sidebar.CreateButton( "Create Road", "route", null, CreateRoad, true, row );
		IconButton inter = sidebar.CreateButton( "Create Intersection", "roundabout_left", null, CreateIntersection, true, row );

		row.Spacing = 5;
		row.AddStretchCell();

		group.Add( row );

		sidebar.Layout.Add( group );
		sidebar.Layout.AddStretchCell();
		return sidebar;
	}
	
	private static void CreateRoad()
	{
		GameObject go = SceneEditorSession.Active.Scene.CreateObject();
		go.Name = "Road";
		go.AddComponent<RoadComponent>();
	}

	private static void CreateIntersection()
	{
		GameObject go = SceneEditorSession.Active.Scene.CreateObject();
		go.Name = "Road Intersection";
		go.AddComponent<RoadIntersectionComponent>();
	}
}
