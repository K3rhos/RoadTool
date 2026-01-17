using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	private static Vector3 ParallelTransport(Vector3 _Up, Vector3 _PreviousTangent, Vector3 _CurrentTangent)
	{
		Vector3 rotationAxis = Vector3.Cross(_PreviousTangent, _CurrentTangent);
		float dotProduct = Vector3.Dot(_PreviousTangent, _CurrentTangent);
		float angle = MathF.Acos(dotProduct.Clamp(-1.0f, 1.0f));

		if (rotationAxis.LengthSquared > 0.0001f && angle > 0.0001f)
		{
			rotationAxis = rotationAxis.Normal;

			_Up = RotateVectorAroundAxis(_Up, rotationAxis, angle);
		}

		return _Up;
	}



	private static Vector3 RotateVectorAroundAxis(Vector3 _Vector, Vector3 _Axis, float _Angle)
	{
		Quaternion rotation = Quaternion.CreateFromAxisAngle(_Axis, _Angle);

		return System.Numerics.Vector3.Transform(_Vector, rotation);
	}



	private static Transform[] CalculateTangentFramesUsingUpDir(Spline _Spline, int _FrameCount)
	{
		var frames = new Transform[_FrameCount];

		float totalSplineLength = _Spline.Length;

		var sample = _Spline.SampleAtDistance(0.0f);
		sample.Up = Vector3.Up;

		// Choose an initial up vector if tangent is parallel to Up
		if (MathF.Abs(Vector3.Dot(sample.Tangent, sample.Up)) > 0.999f)
		{
			sample.Up = Vector3.Right;
		}

		for (int i = 0; i < _FrameCount; i++)
		{
			float t = 0.0f;

			if (_FrameCount > 1)
				t = (float)i / (_FrameCount - 1);

			float distance = t * totalSplineLength;

			sample = _Spline.SampleAtDistance(distance);

			// Apply roll
			var newUp = Rotation.FromAxis(sample.Tangent, sample.Roll) * sample.Up;

			Rotation rotation = Rotation.LookAt(sample.Tangent, newUp);

			frames[i] = new Transform(sample.Position, rotation, sample.Scale);
		}

		return frames;
	}



	private static Transform[] CalculateRotationMinimizingTangentFrames(Spline _Spline, int _FrameCount)
	{
		var frames = new Transform[_FrameCount];

		float totalSplineLength = _Spline.Length;

		// Initialize the up vector
		var previousSample = _Spline.SampleAtDistance(0.0f);
		Vector3 up = Vector3.Up;

		// Choose an initial up vector if tangent is parallel to Up
		if (MathF.Abs(Vector3.Dot(previousSample.Tangent, up)) > 0.999f)
		{
			up = Vector3.Right;
		}

		up = Rotation.FromAxis(previousSample.Tangent, previousSample.Roll) * up;

		frames[0] = new Transform(previousSample.Position, Rotation.LookAt(previousSample.Tangent, up), previousSample.Scale);

		for (int i = 1; i < _FrameCount; i++)
		{
			float t = 0.0f;

			if (_FrameCount > 1)
				t = (float)i / (_FrameCount - 1);

			float distance = t * totalSplineLength;

			var sample = _Spline.SampleAtDistance(distance);

			// Parallel transport the up vector
			up = GetRotationMinimizingNormal(previousSample.Position, previousSample.Tangent, up, sample.Position, sample.Tangent);

			// Apply roll
			float deltaRoll = sample.Roll - previousSample.Roll;
			up = Rotation.FromAxis(sample.Tangent, deltaRoll) * up;

			Rotation rotation = Rotation.LookAt(sample.Tangent, up);

			frames[i] = new Transform(sample.Position, rotation, sample.Scale);

			previousSample = sample;
		}

		// Correct up vectors for looped splines
		if (_Spline.IsLoop && frames.Length > 1)
		{
			Vector3 startUp = frames[0].Rotation.Up;
			Vector3 endUp = frames[^1].Rotation.Up;

			float theta = MathF.Acos(Vector3.Dot(startUp, endUp)) / (frames.Length - 1);
			if (Vector3.Dot(frames[0].Rotation.Forward, Vector3.Cross(startUp, endUp)) > 0)
			{
				theta = -theta;
			}

			for (int i = 0; i < frames.Length; i++)
			{
				Rotation R = Rotation.FromAxis(frames[i].Rotation.Forward, (theta * i).RadianToDegree());
				Vector3 correctedUp = R * frames[i].Rotation.Up;
				frames[i] = new Transform(frames[i].Position, Rotation.LookAt(frames[i].Rotation.Forward, correctedUp), frames[i].Scale);
			}
		}

		return frames;
	}



	private static Vector3 GetRotationMinimizingNormal(Vector3 _PosA, Vector3 _TangentA, Vector3 _NormalA, Vector3 _PosB, Vector3 _TangentB)
	{
		// Source: https://www.microsoft.com/en-us/research/wp-content/uploads/2016/12/Computation-of-rotation-minimizing-frames.pdf
		Vector3 v1 = _PosB - _PosA;

		float v1DotV1Half = Vector3.Dot(v1, v1) / 2.0f;
		float r1 = Vector3.Dot(v1, _NormalA) / v1DotV1Half;
		float r2 = Vector3.Dot(v1, _TangentA) / v1DotV1Half;

		Vector3 nL = _NormalA - r1 * v1;
		Vector3 tL = _TangentA - r2 * v1;
		Vector3 v2 = _TangentB - tL;

		float r3 = Vector3.Dot(v2, nL) / Vector3.Dot(v2, v2);

		return (nL - 2f * r3 * v2).Normal;
	}



	private static List<int> DetectImportantSegments(Transform[] _Frames, int _SegmentCount, int _MinSegmentsToMerge, float _StraightThreshold)
	{
		List<int> important =
		[
			0 // Always keep first
		];

		var straightRun = new List<int>();

		for (int i = 1; i < _SegmentCount; i++)
		{
			float angle = CalculateAngleBetweenSegments(_Frames[i - 1], _Frames[i], _Frames[i + 1]);

			// This segment is straight
			if (angle < _StraightThreshold)
			{
				straightRun.Add(i);
			}

			// This segment has curvature
			else
			{
				if (straightRun.Count < _MinSegmentsToMerge)
				{
					important.AddRange(straightRun);
				}

				straightRun.Clear();

				// Keep this curved point
				important.Add(i);
			}
		}

		// Handle remaining straight run at the end
		if (straightRun.Count > 0 && straightRun.Count < _MinSegmentsToMerge)
		{
			important.AddRange(straightRun);
		}

		// Always keep last
		important.Add(_SegmentCount);

		// Remove duplicates and sort
		important = important.Distinct().OrderBy(x => x).ToList();

		return important;
	}
	
	
	
	private static float CalculateAngleBetweenSegments(Transform _Previous, Transform _Current, Transform _Next)
	{
		Vector3 dir1 = (_Current.Position - _Previous.Position).Normal;
		Vector3 dir2 = (_Next.Position - _Current.Position).Normal;

		if (dir1.IsNearZeroLength || dir2.IsNearZeroLength)
			return 0.0f;

		float dot = Vector3.Dot(dir1, dir2).Clamp(-1.0f, 1.0f);
		float angleRadians = MathF.Acos(dot);

		return angleRadians.RadianToDegree();
	}
}
