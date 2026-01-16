using System.Collections.Generic;
using Editor;

namespace RedSnail.RoadTool.Editor;

[EditorTool]
[Title("Road Tool")]
[Icon("signpost")]
[Alias("road")]
[Group("Tools")]
public class RoadEditorTool : EditorTool
{
	public override IEnumerable<EditorTool> GetSubtools()
	{
		yield return new IntersectionTool();
		// yield return new SplineEditorTool();
	}



	public override void OnEnabled()
	{
		AllowGameObjectSelection = false;
	}



	public override void OnDisabled()
	{

	}



	public override void OnUpdate()
	{

	}
}
