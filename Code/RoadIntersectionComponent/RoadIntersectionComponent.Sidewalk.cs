using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// How much of a junction reaches the pedestrian graph. Geometry is never affected — the pavement mesh builds
/// the same either way; this only decides what pedestrians are allowed to route over.
///
/// Ordered most-to-least deliberately, so the zero value is the harmless one: a component that somehow arrives
/// without this field set behaves like every other junction rather than silently vanishing from the graph.
/// </summary>
public enum SidewalkGraphMode
{
	/// <summary>Pavement round the corners and a crossing at every arm.</summary>
	All,

	/// <summary>
	/// Corners only. The pavement stays joined all the way round and connects to every road that meets here,
	/// but there's nothing to step off the kerb onto — pedestrians walk round the junction instead of over it.
	/// </summary>
	NoCrossing,

	/// <summary>
	/// Nothing at all. The roads that meet here keep their own pavements; they just stop being connected
	/// THROUGH this junction, so expect their ends to show as dead ends.
	/// </summary>
	None
}



public partial class RoadIntersectionComponent
{
	/// <summary>
	/// What this junction contributes to the pedestrian graph — the pedestrian counterpart of
	/// <see cref="ExcludeTraffic"/>.
	///
	/// <see cref="SidewalkGraphMode.None"/> for somewhere nobody should be walking round at all: a slip road, a
	/// service yard, a junction whose pavement exists only because the mesh needs an edge.
	/// <see cref="SidewalkGraphMode.NoCrossing"/> for somewhere they may walk past but not across — a forecourt
	/// or car park entrance, where the kerb should stay continuous and stepping into the vehicle route is the
	/// thing you're trying to prevent.
	/// </summary>
	[Property, Feature("General"), Category("Sidewalk"), Order(3)] public SidewalkGraphMode SidewalkGraph { get; set; } = SidewalkGraphMode.All;



	/// <summary>
	/// The walking line around this junction's pavement, in world space — a closed loop running down the
	/// middle of the sidewalk slab.
	///
	/// Built from the junction's OWN outline, which is the whole point: a rectangular intersection's pavement
	/// runs along its edges and turns at its corners, and approximating that with an arc around the centre
	/// bows the path out into the road at the middle of each side and cuts the corners off. A circle really is
	/// an arc, so it gets one.
	///
	/// The loop runs all the way round, arm mouths included — whoever consumes it is expected to split it at
	/// the kerbs, because where the pavement is interrupted is the same question as where the crossings go.
	/// </summary>
	public List<Vector3> GetSidewalkOutline(float _Spacing)
	{
		var points = new List<Vector3>();

		if (!HasSidewalks)
			return points;

		float spacing = Math.Max(1.0f, _Spacing);

		// Centre of the slab, so it sits where someone would actually walk rather than on either kerb.
		float outset = SidewalkWidth * 0.5f;
		Vector3 lift = Vector3.Up * SidewalkHeight;

		if (Shape == IntersectionShape.Circle)
		{
			float radius = Radius + outset;
			int steps = Math.Max(8, (int)MathF.Ceiling(MathF.Tau * radius / spacing));

			for (int i = 0; i < steps; i++)
			{
				float angle = MathF.Tau * i / steps;

				points.Add(new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0.0f) + lift);
			}
		}
		else
		{
			// Written with the same right/forward vectors BuildRectangleRoad uses, rather than as raw x/y
			// components. Width runs along Right and Length along Forward, which in s&box axes is Y and X —
			// spelling that out by hand gets them the wrong way round, and a junction outline rotated 90°
			// looks almost plausible until nothing connects to it.
			Vector3 right = Vector3.Right;
			Vector3 forward = Vector3.Forward;

			float hw = Width * 0.5f + outset;
			float hl = Length * 0.5f + outset;

			// Round the rectangle in order, so the loop has a consistent winding for anything that walks it.
			Vector3[] corners =
			[
				-right * hw - forward * hl,
				 right * hw - forward * hl,
				 right * hw + forward * hl,
				-right * hw + forward * hl,
			];

			for (int i = 0; i < corners.Length; i++)
			{
				Vector3 from = corners[i];
				Vector3 to = corners[(i + 1) % corners.Length];

				int steps = Math.Max(1, (int)MathF.Ceiling(Vector3.DistanceBetween(from, to) / spacing));

				// Last point of each edge is skipped — it's the first of the next one, and a closed loop
				// mustn't repeat its corners.
				for (int s = 0; s < steps; s++)
					points.Add(Vector3.Lerp(from, to, (float)s / steps) + lift);
			}
		}

		for (int i = 0; i < points.Count; i++)
			points[i] = WorldTransform.PointToWorld(points[i]);

		return points;
	}



	/// <summary>Whether this junction has pavement to walk on at all.</summary>
	public bool HasSidewalks => SidewalkWidth > 0.0f;
}
