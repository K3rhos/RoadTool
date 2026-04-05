using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadIntersectionComponent
{
	[Property, FeatureEnabled("Physics", Icon = "tire_repair", Tint = EditorTint.Red), Change]
	private bool HasCustomPhysics { get; set; } = false;

	[Property(Title = "Surface Material"), Feature("Physics")]
	public Surface IntersectionSurface { get; set { field = value; m_MeshBuilder?.IsDirty = true; } }

	private void OnHasCustomPhysicsChanged(bool _OldValue, bool _NewValue)
	{
		if (m_MeshBuilder != null) m_MeshBuilder.IsDirty = true;
	}
}
