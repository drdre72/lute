namespace Lute.UI;

/// <summary>
/// First/third person camera switching for the player.
/// Press C to toggle between first-person and third-person views.
/// In first-person, the camera is at eye level.
/// In third-person, the camera is behind and above the player.
/// </summary>
public sealed class LuteCamera : Component
{
	[RequireComponent] public CameraComponent Camera { get; set; }

	/// <summary> Is first-person mode active? </summary>
	[Property] public bool FirstPerson { get; set; } = false;

	/// <summary> Third-person camera distance behind player. </summary>
	[Property] public float ThirdPersonDistance { get; set; } = 120f;

	/// <summary> Third-person camera height above player. </summary>
	[Property] public float ThirdPersonHeight { get; set; } = 40f;

	/// <summary> First-person eye height. </summary>
	[Property] public float EyeHeight { get; set; } = 64f;

	/// <summary> Smooth camera movement speed. </summary>
	[Property] public float LerpSpeed { get; set; } = 10f;

	private Vector3 _targetPos;
	private Rotation _targetRot;

	protected override void OnUpdate()
	{
		// Toggle camera with C (View action)
		if ( Input.Pressed( "View" ) )
		{
			FirstPerson = !FirstPerson;
			Log.Info( $"Lute: Camera switched to {(FirstPerson ? "first-person" : "third-person")}" );
		}

		// Get the player's controller for eye angles
		var controller = Components.GetOrCreate<Sandbox.PlayerController>();
		if ( controller == null )
			return;

		var eyeAngles = controller.EyeAngles;
		_targetRot = eyeAngles.ToRotation();

		if ( FirstPerson )
		{
			// First-person: camera at eye level
			_targetPos = controller.WorldPosition + Vector3.Up * EyeHeight;
		}
		else
		{
			// Third-person: camera behind and above player
			var forward = _targetRot.Forward;
			var back = -forward * ThirdPersonDistance;
			var up = Vector3.Up * ThirdPersonHeight;
			_targetPos = controller.WorldPosition + back + up;
		}

		// Smooth lerp
		Camera.WorldPosition = Camera.WorldPosition.LerpTo( _targetPos, Time.Delta * LerpSpeed );
		Camera.WorldRotation = Rotation.Slerp( Camera.WorldRotation, _targetRot, Time.Delta * LerpSpeed );
	}
}
