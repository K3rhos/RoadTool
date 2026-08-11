using System;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RailComponent
{
	private static Transform[] CalculateTangentFramesUsingUpDir(Spline _Spline, int _FrameCount)
	{
		var frames = new Transform[_FrameCount];
		float totalSplineLength = _Spline.Length;

		var sample = _Spline.SampleAtDistance(0.0f);
		sample.Up = Vector3.Up;

		// Fall back to a different up if the tangent runs (nearly) straight up.
		if (MathF.Abs(Vector3.Dot(sample.Tangent, sample.Up)) > 0.999f)
			sample.Up = Vector3.Right;

		for (int i = 0; i < _FrameCount; i++)
		{
			float t = _FrameCount > 1 ? (float)i / (_FrameCount - 1) : 0.0f;
			float distance = t * totalSplineLength;

			sample = _Spline.SampleAtDistance(distance);

			var up = Rotation.FromAxis(sample.Tangent, sample.Roll) * sample.Up;
			Rotation rotation = Rotation.LookAt(sample.Tangent, up);

			frames[i] = new Transform(sample.Position, rotation, sample.Scale);
		}

		return frames;
	}



	private static Transform[] CalculateRotationMinimizingTangentFrames(Spline _Spline, int _FrameCount)
	{
		var frames = new Transform[_FrameCount];
		float totalSplineLength = _Spline.Length;

		var previousSample = _Spline.SampleAtDistance(0.0f);
		Vector3 up = Vector3.Up;

		if (MathF.Abs(Vector3.Dot(previousSample.Tangent, up)) > 0.999f)
			up = Vector3.Right;

		up = Rotation.FromAxis(previousSample.Tangent, previousSample.Roll) * up;

		frames[0] = new Transform(previousSample.Position, Rotation.LookAt(previousSample.Tangent, up), previousSample.Scale);

		for (int i = 1; i < _FrameCount; i++)
		{
			float t = _FrameCount > 1 ? (float)i / (_FrameCount - 1) : 0.0f;
			float distance = t * totalSplineLength;

			var sample = _Spline.SampleAtDistance(distance);

			// Parallel-transport the up vector so the profile doesn't twist through 3D curves.
			up = GetRotationMinimizingNormal(previousSample.Position, previousSample.Tangent, up, sample.Position, sample.Tangent);

			float deltaRoll = sample.Roll - previousSample.Roll;
			up = Rotation.FromAxis(sample.Tangent, deltaRoll) * up;

			Rotation rotation = Rotation.LookAt(sample.Tangent, up);
			frames[i] = new Transform(sample.Position, rotation, sample.Scale);

			previousSample = sample;
		}

		return frames;
	}



	private static Vector3 GetRotationMinimizingNormal(Vector3 _PosA, Vector3 _TangentA, Vector3 _NormalA, Vector3 _PosB, Vector3 _TangentB)
	{
		// Source: https://www.microsoft.com/en-us/research/wp-content/uploads/2016/12/Computation-of-rotation-minimizing-frames.pdf
		Vector3 v1 = _PosB - _PosA;

		float v1DotV1Half = Vector3.Dot(v1, v1) / 2.0f;

		if (v1DotV1Half <= 0.0001f)
			return _NormalA;

		float r1 = Vector3.Dot(v1, _NormalA) / v1DotV1Half;
		float r2 = Vector3.Dot(v1, _TangentA) / v1DotV1Half;

		Vector3 nL = _NormalA - r1 * v1;
		Vector3 tL = _TangentA - r2 * v1;
		Vector3 v2 = _TangentB - tL;

		float r3 = Vector3.Dot(v2, nL) / Vector3.Dot(v2, v2);

		return (nL - 2.0f * r3 * v2).Normal;
	}
}
