/// <summary>
/// Lute mage player. Sits alongside the built-in <see cref="PlayerController"/>
/// and adds spell-casting input hooks for the eight Lute spell schools.
/// Movement (WASD/sprint/jump/mouse-look) is handled by the PlayerController.
/// </summary>
public sealed class LutePlayer : Component
{
	[RequireComponent] public PlayerController Controller { get; set; }

	/// <summary> Currently selected spell school. </summary>
	[Property] public SpellType SelectedSpell { get; set; } = SpellType.Fire;

	/// <summary> Whether noclip (fly) mode is active. Toggle with V. </summary>
	[Property] public bool NoclipEnabled { get; set; } = false;

	private Rigidbody _body;
	private List<Collider> _colliders = new();

	protected override void OnStart()
	{
		_body = Controller.GetComponent<Rigidbody>();
		// Collect all colliders on the player hierarchy so we can disable them for noclip
		_colliders = Controller.GameObject.GetComponentsInChildren<Collider>( true, true ).ToList();
	}

	protected override void OnUpdate()
	{
		// Toggle noclip with V
		if ( Input.Pressed( "noclip" ) )
		{
			NoclipEnabled = !NoclipEnabled;
			ApplyNoclip();
			Log.Info( $"Lute: noclip {(NoclipEnabled ? "enabled" : "disabled")}" );
		}

		// Noclip movement: fly with WASD + space/ctrl for up/down
		if ( NoclipEnabled && _body != null )
		{
			var speed = Input.Down( "run" ) ? 1200f : 400f;
			var input = Input.AnalogMove;
			var wishDir = Controller.EyeAngles.ToRotation() * input;

			// Up/down with jump (space) and duck (ctrl)
			if ( Input.Down( "jump" ) ) wishDir += Vector3.Up;
			if ( Input.Down( "duck" ) ) wishDir -= Vector3.Up;

			_body.Velocity = wishDir.Normal * speed;
		}

		// Cycle through the eight spell schools with slot keys 1-8.
		for ( int i = 0; i < 8; i++ )
		{
			if ( Input.Pressed( $"slot{i + 1}" ) )
				SelectedSpell = (SpellType)i;
		}

		// Cast the selected spell on primary attack (mouse1).
		if ( Input.Pressed( "attack1" ) )
		{
			Log.Info( $"Lute: cast {SelectedSpell} at {Controller.WorldPosition}" );
		}
	}

	void ApplyNoclip()
	{
		if ( _body != null )
		{
			_body.Gravity = !NoclipEnabled;
			_body.LinearDamping = NoclipEnabled ? 5f : 0.1f;
		}

		// Disable/enable all colliders so we can pass through walls
		foreach ( var collider in _colliders )
		{
			collider.Enabled = !NoclipEnabled;
		}
	}
}
