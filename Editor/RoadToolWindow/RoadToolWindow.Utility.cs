using System;
using Editor;

namespace RedSnail.RoadTool.Editor;

public partial class RoadToolWindow
{
	/// <summary>
	/// Describes how the spline should behave when entering/leaving a point.
	/// </summary>
	public enum HandleModeTemp
	{
		/// <summary>
		/// Handle positions are calculated automatically based on the location of adjacent points.
		/// </summary>
		[Icon("auto_fix_high")]
		Auto,

		/// <summary>
		/// Handle positions are set to zero, leading to a sharp corner.
		/// </summary>
		[Icon("show_chart")]
		Linear,

		/// <summary>
		/// The In and Out handles are user set, but are linked (mirrored).
		/// </summary>
		[Icon("open_in_full")]
		Mirrored,

		/// <summary>
		/// The In and Out handle are user set and operate independently.
		/// </summary>
		[Icon("call_split")]
		Split,
	}



	private IDisposable CreateUndoScope(string _Name)
	{
		return SceneEditorSession.Active.UndoScope(_Name).WithComponentChanges(_targetComponent).Push();
	}
}
