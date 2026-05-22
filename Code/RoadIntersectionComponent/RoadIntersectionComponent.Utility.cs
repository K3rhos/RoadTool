using System;

namespace RedSnail.RoadTool;

public partial class RoadIntersectionComponent
{
	// Computes a quadratic Bezier control point at the intersection of the two tangent lines.
	// Returns true if the lines intersect, false if parallel (in which case the midpoint is used as a fallback).
	// The control distance along _StartTan is clamped to the chord length so asymmetric tangents (e.g. an
	// off-grid exit angle whose disc tangent points well past the outer corner) don't drive the bezier past
	// the outer endpoint — overshoot produces samples beyond the endpoint and flips downstream triangle winding.
	private static bool TryBezierControl(Vector3 _Start, Vector3 _StartTan, Vector3 _End, Vector3 _EndTan, out Vector3 _Control)
	{
		float det = _StartTan.x * _EndTan.y - _EndTan.x * _StartTan.y;

		if (MathF.Abs(det) < 0.0001f)
		{
			_Control = (_Start + _End) * 0.5f;
			return false;
		}

		Vector3 d = _End - _Start;
		float r = (d.x * _EndTan.y - _EndTan.x * d.y) / det;
		float rMax = d.Length;
		float rClamped = Math.Clamp(r, 0.0f, rMax);
		_Control = _Start + rClamped * _StartTan;
		return true;
	}

	private static Vector3 SampleQuadBezier(Vector3 _B0, Vector3 _B1, Vector3 _B2, float _T)
	{
		float u = 1.0f - _T;
		return u * u * _B0 + 2.0f * u * _T * _B1 + _T * _T * _B2;
	}
}
