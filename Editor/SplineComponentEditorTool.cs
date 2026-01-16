using Sandbox;
using Editor;

namespace RedSnail.RoadTool.Editor;

/// <summary>
/// Create and manage road splines.
/// </summary>
[Title("Road Splines")]
[Icon("timeline")]
[Alias("road_splines")]
[Group("1")]
[Order(1)]
public class SplineEditorTool : EditorTool<RoadComponent>
{
	private RoadToolWindow m_Window;
	private RoadComponent m_SelectedRoadComponent;



	public override void OnEnabled()
	{
		m_Window = new RoadToolWindow();
		AddOverlay(m_Window, TextFlag.RightBottom, 10);
	}



	public override void OnDisabled()
	{
		m_Window?.OnDisabled();
	}



	public override void OnUpdate()
	{
		m_Window?.OnUpdate();
	}



	public override void OnSelectionChanged()
	{
		RoadComponent target = GetSelectedComponent<RoadComponent>();

		if (!target.IsValid())
			return;

		// Fix because otherwise it get triggered everytime a value get edited on the road spline
		if (target != m_SelectedRoadComponent)
		{
			m_Window?.OnSelectionChanged(target);

			m_SelectedRoadComponent = target;
		}
	}
}
