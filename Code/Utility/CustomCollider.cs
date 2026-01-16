using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public sealed class CustomCollider : Collider
{
	private List<Vector3> m_Vertices;
	private List<int> m_Indices;

	private bool IsDirty { get; set; }



	protected override void OnUpdate()
	{
		if (!IsDirty)
			return;

		UpdateCollisions();
	}



	private PhysicsShape UpdateCollisions()
	{
		if (m_Vertices.Count == 0)
			return null;

		if (m_Indices.Count == 0)
			return null;

		PhysicsBody physicsBody = Rigidbody.IsValid() ? Rigidbody.PhysicsBody : KeyframeBody;

		if (!physicsBody.IsValid())
			return null;

		// Clear existing shapes
		physicsBody.ClearShapes();

		// Add mesh shape to physics body
		PhysicsShape physicsShape = physicsBody.AddMeshShape(m_Vertices, m_Indices);

		IsDirty = false;

		return physicsShape;
	}



	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes(PhysicsBody _TargetBody, Transform _Local)
	{
		yield return UpdateCollisions();
	}



	// We don't want this since it can cause issue
	protected override void RebuildImmediately() { }



	public void SetMeshShape(List<Vector3> _Vertices, List<int> _Indices)
	{
		m_Vertices = _Vertices;
		m_Indices = _Indices;

		IsDirty = true;
	}
}
