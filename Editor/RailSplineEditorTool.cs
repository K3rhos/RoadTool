using Editor;
using Sandbox;

namespace RedSnail.RoadTool.Editor;

/// <summary>
/// Create and manage rail splines — the same spline point editor overlay the road uses, bound to <see cref="RailComponent"/>.
/// </summary>
[Title("Rail Splines")]
[Icon("timeline")]
[Alias("rail_splines")]
[Group("1")]
[Order(2)]
public class RailSplineEditorTool : EditorTool<RailComponent>
{
	private RoadToolWindow m_Window;
	private RailComponent m_SelectedRailComponent;



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
		RailComponent target = GetSelectedComponent<RailComponent>();

		if (!target.IsValid())
			return;

		// Avoid re-triggering every time a value is edited on the rail spline.
		if (target != m_SelectedRailComponent)
		{
			m_Window?.OnSelectionChanged(target);

			m_SelectedRailComponent = target;
		}
	}
}
