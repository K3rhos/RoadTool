using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	[Property, FeatureEnabled( "Physics", Icon = "tire_repair", Tint = EditorTint.Red ), Change]
	private bool HasCustomPhysics { get; set; } = false;

	[Property( Title = "Surface Material" ), Feature( "Physics" )]
	public Surface RoadSurface { get; set { field = value; IsDirty = true; } }

	private void OnHasCustomPhysicsChanged( bool _OldValue, bool _NewValue )
	{
		IsDirty = true;
	}
}
