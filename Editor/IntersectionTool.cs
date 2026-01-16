using Sandbox;
using Editor;

namespace RedSnail.RoadTool.Editor;

/// <summary>
/// Create and manage road and road intersection.
/// </summary>
[Title("Create Road/Intersection")]
[Icon("roundabout_left")]
[Alias("intersection")]
[Group("1")]
[Order(0)]
public class IntersectionTool : EditorTool
{
	public override Widget CreateToolSidebar()
	{
		var sidebar = new ToolSidebarWidget();
		
		sidebar.MaximumSize = Vector2.One * 120.0f;
		
		var group = sidebar.AddGroup("Create");

		sidebar.CreateButton("Create Road", "route", null, CreateRoad, true, group);
		sidebar.CreateButton("Create Intersection", "roundabout_left", null, CreateIntersection, true, group);

		return sidebar;
	}



	public override void OnEnabled()
	{
		
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
