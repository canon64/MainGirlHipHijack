using System;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGirlShoulderIkStabilizer;

internal sealed class ShoulderRotator : MonoBehaviour
{
	private FullBodyBipedIK _ik;

	private Transform _chaRoot;

	private PluginSettings _settings;


	private bool _hooked;

	internal void Configure(FullBodyBipedIK ik, Transform chaRoot, PluginSettings settings)
	{
		if ((object)_ik != ik)
		{
			UnhookSolver();
			_ik = ik;
			HookSolver();
		}
		_chaRoot = chaRoot;
		_settings = settings;
	}

private void OnEnable()
	{
		HookSolver();
	}

	private void OnDisable()
	{
		UnhookSolver();
	}

	private void OnDestroy()
	{
		UnhookSolver();
	}

	private void HookSolver()
	{
		if (!_hooked && !(_ik == null) && _ik.solver != null)
		{
			IKSolverFullBodyBiped solver = _ik.solver;
			solver.OnPostUpdate = (IKSolver.UpdateDelegate)Delegate.Combine(solver.OnPostUpdate, new IKSolver.UpdateDelegate(RotateShoulders));
			_hooked = true;
		}
	}

	private void UnhookSolver()
	{
		if (!_hooked || _ik == null || _ik.solver == null)
		{
			_hooked = false;
			return;
		}
		IKSolverFullBodyBiped solver = _ik.solver;
		solver.OnPostUpdate = (IKSolver.UpdateDelegate)Delegate.Remove(solver.OnPostUpdate, new IKSolver.UpdateDelegate(RotateShoulders));
		_hooked = false;
	}

	private void RotateShoulders()
	{
		if (_ik == null || _settings == null || !_settings.ShoulderRotationEnabled)
		{
			return;
		}
		IKSolver solver = _ik.solver;
		if (solver != null && !(solver.IKPositionWeight <= 0f))
		{
			float leftWeight = _settings.ShoulderWeight;
			float leftOffset = _settings.ShoulderOffset;
			float rightWeight = (_settings.IndependentShoulders ? _settings.ShoulderRightWeight : _settings.ShoulderWeight);
			float rightOffset = (_settings.IndependentShoulders ? _settings.ShoulderRightOffset : _settings.ShoulderOffset);
			RotateShoulder(FullBodyBipedChain.LeftArm, leftWeight, leftOffset, _settings.ReverseShoulderL);
			RotateShoulder(FullBodyBipedChain.RightArm, rightWeight, rightOffset, _settings.ReverseShoulderR);
		}
	}

	private void RotateShoulder(FullBodyBipedChain chain, float weight, float offset, bool reverseWhenLowered)
	{
		if (_ik == null || _ik.solver == null)
		{
			return;
		}
		IKSolverFullBodyBiped solver = _ik.solver;
		IKMappingLimb limbMapping = solver.GetLimbMapping(chain);
		IKEffector endEffector = solver.GetEndEffector(chain);
		FBIKChain chainData = solver.GetChain(chain);
		IKMapping.BoneMap parentBoneMap = GetParentBoneMap(chain);
		if (limbMapping == null || endEffector == null || chainData == null || parentBoneMap == null || limbMapping.bone1 == null || limbMapping.parentBone == null || parentBoneMap.transform == null || chainData.nodes == null || chainData.nodes.Length < 2)
		{
			return;
		}
		bool lowered = IsArmLowered(endEffector, limbMapping);
		float raised01 = GetRaisedArm01(endEffector, limbMapping);
		if (lowered)
		{
			float scale = Mathf.Clamp01(_settings.LoweredArmScale);
			weight *= scale;
			offset *= scale;
		}
		else if (raised01 > 0f)
		{
			float scale2 = Mathf.Lerp(1f, Mathf.Clamp01(_settings.RaisedArmScaleMin), raised01);
			weight *= scale2;
			offset *= scale2;
		}
		Vector3 toTarget = endEffector.position - parentBoneMap.transform.position;
		Quaternion fromTo = Quaternion.FromToRotation(parentBoneMap.swingDirection, toTarget);
		Vector3 limbVector = endEffector.position - limbMapping.bone1.position;
		float chainLength = chainData.nodes[0].length + chainData.nodes[1].length;
		if (chainLength <= 0.0001f)
		{
			return;
		}
		float solverBlend = Mathf.Min(Mathf.Clamp((limbVector.magnitude / chainLength - 1f + offset) * weight, 0f, 1f), Mathf.Clamp01(_settings.MaxSolverBlend)) * endEffector.positionWeight * solver.IKPositionWeight;
		Quaternion delta = Quaternion.Lerp(Quaternion.identity, fromTo, solverBlend);
		if (lowered && reverseWhenLowered)
		{
			delta = Quaternion.Inverse(delta);
		}
		float maxDelta = _settings.MaxShoulderDeltaAngleDeg;
		if (maxDelta > 0f)
		{
			float angle = Quaternion.Angle(Quaternion.identity, delta);
			if (angle > maxDelta && angle > 0.0001f)
			{
				float t = maxDelta / angle;
				delta = Quaternion.Slerp(Quaternion.identity, delta, t);
			}
		}
		limbMapping.parentBone.rotation = delta * limbMapping.parentBone.rotation;
	}

	private IKMapping.BoneMap GetParentBoneMap(FullBodyBipedChain chain)
	{
		if (_ik == null || _ik.solver == null)
		{
			return null;
		}
		return _ik.solver.GetLimbMapping(chain)?.GetBoneMap(IKMappingLimb.BoneMapType.Parent);
	}

	private bool IsArmLowered(IKEffector endEffector, IKMappingLimb limbMapping)
	{
		return GetLocalArmYDelta(endEffector, limbMapping) < 0f;
	}

	private float GetRaisedArm01(IKEffector endEffector, IKMappingLimb limbMapping)
	{
		float yDelta = GetLocalArmYDelta(endEffector, limbMapping);
		float start = _settings.RaisedArmStartY;
		if (yDelta <= start)
		{
			return 0f;
		}
		float full = Mathf.Max(start + 0.0001f, _settings.RaisedArmFullY);
		return Mathf.Clamp01((yDelta - start) / (full - start));
	}

private float GetLocalArmYDelta(IKEffector endEffector, IKMappingLimb limbMapping)
	{
		if (_chaRoot == null || endEffector == null || limbMapping == null || limbMapping.bone1 == null)
		{
			return 0f;
		}
		Vector3 vector = _chaRoot.InverseTransformPoint(endEffector.position);
		Vector3 upperLocal = _chaRoot.InverseTransformPoint(limbMapping.bone1.position);
		return (vector - upperLocal).y;
	}
}
