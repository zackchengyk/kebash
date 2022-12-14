using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class Movement : MonoBehaviour
{
  private PlayerInputData _inputData;
  
  [SerializeField] private GameObject _damagerObject;
  [SerializeField] private Rigidbody  _rigidbody;
  [SerializeField] private Animator   _animator;
  [SerializeField] private StaminaBar _staminaBar;
  [SerializeField] private ParticleSystem _stabParticles;
  [SerializeField] private ParticleSystem _dashParticles;
  [SerializeField] private ParticleSystem _walkParticles;

  // Health
  private bool  _isInvulnerable = false;
  private float _hitInvDuration = 1f;
  private IEnumerator _currentInvInstance;
  private int _debugCount = 0;

  // Food Stuff
  private int _maxFood = 3;
  [SerializeField] private List<Transform> _foodSliceTransforms;
  [SerializeField] private GameObject _foodBulletPrefab;

  // Food Bullet Prefabs
  [SerializeField] private GameObject _lambPrefab;
  [SerializeField] private GameObject _onionPrefab;
  [SerializeField] private GameObject _tomatoPrefab;
  [SerializeField] private GameObject _mushroomPrefab;
  [SerializeField] private GameObject _pepperPrefab;
  [SerializeField] private GameObject _eggplantPrefab;

  // Moving and turning
  private Vector3 _idealMove   = Vector3.zero;
  private Vector3 _idealTurn   = Vector3.zero;
  private Vector3 _currentMove = Vector3.zero;
  private Vector3 _currentTurn = Vector3.zero;
  private float _moveSpeed             = 7f;
  private float _moveLerpRate          = 0.2f;
  private float _turnSlerpRate         = 0.08f;
  private float _strafeSpeedMultiplier = 0.5f;

  // Charging
  private float _stamina;
  private float _minChargeDuration;
  public float  _maxStamina        = 100f;
  private float _minChargeCost     = 10f;  // Minimum stamina cost associated with one charge
  private float _minStaminaToStart = 20f;  // Minimum stamina needed to start a charge
  private float _staminaCostRate   = 100f;
  public float  _staminaRegenRate  = 40f;
  private float _regenDelay        = 0.15f; // Delay after charging ends before regen begins
  private bool  _canRegen    = true;
  private float _chargeSpeed = 14f;

  // Shooting
  private float _shootCoolDown = 1f;
  private bool  _isOnShootCoolDown = false;

  // Falling
  private float _fallRespawnWaitDuration = 1; // Delay after dying before falling back down
  private float _fallInvDuration = 2;         // How long the player is invulnerable after teleporting back up

  // ================== Accessors

  public float StaminaFraction { get { return _stamina / _maxStamina; } }

  public int PlayerIndex { get; set; } = -1;

  public Rigidbody Rigidbody { get { return _rigidbody; } }

  public Vector3 RespawnPosition { get; set; } = new Vector3(0, 100, 0);
  public bool IsCharging { get; private set; } = false;
  public bool IsOnGround { get; private set; } = false;
  public Stack<PooledObjectIndex> KebabStack { get; private set; } = new Stack<PooledObjectIndex>();

  // ================== Methods
  
  void Awake()
  {
    _inputData = GetComponent<PlayerInputScript>().InputData;
  }

  void Start()
  {
    ResetForMain();
  }

  void FixedUpdate()
  {
    updateIsOnGround();

    updateParticles();

    // Force respawn position during menu state
    if (GameStateManager.Instance.State == GameState.Menu)
    {
      _rigidbody.position = RespawnPosition;
      _rigidbody.velocity = Vector3.zero;
      _rigidbody.useGravity = false;
      _animator.SetBool("Charging", false);
      _animator.SetFloat("InputY", 0);
      _animator.SetFloat("InputX", 0);
      return;
    }

    // Prevent horizontal movement during game over state
    if (GameStateManager.Instance.State == GameState.GameOver)
    {
      _rigidbody.velocity = new Vector3(0, _rigidbody.velocity.y, 0);
      _animator.SetBool("Charging", false);
      _animator.SetFloat("InputY", 0);
      _animator.SetFloat("InputX", 0);
      return;
    }

    // Prevent player control in certain situations
    if (IsCharging || !IsOnGround || GameStateManager.Instance.State != GameState.GamePlay) return;

    // Attempt to shoot
    if (_inputData.Shoot && !_isOnShootCoolDown)
    {
      StopCoroutine("shootFood");
      StartCoroutine("shootFood");
      return;
    }

    // Attempt to charge
    if (_inputData.Charge && _stamina > _minStaminaToStart)
    {
      StopCoroutine("charge");
      StartCoroutine("charge");
      return;
    }
    
    if (_inputData.Charge && _stamina <= _minStaminaToStart)
    {
      _staminaBar.shake();
      AudioManager.Instance.Play("nocharge", PlayerIndex);
    }

    move();
    turn();
    regenStamina();

    // Fixme
    float forwardFloat = Vector3.Dot(_currentMove, _currentTurn);
    _animator.SetFloat("InputY", forwardFloat);
    float rightFloat   = Vector3.Dot(_currentMove, Quaternion.AngleAxis(-90, Vector3.up) * _currentTurn);
    _animator.SetFloat("InputX", rightFloat);
  }

  void OnTriggerEnter(Collider other)
  {
    // Fell through fall trigger collider
    if (other.gameObject.layer == Utils.FallTriggerLayer)
    {
      AudioManager.Instance.Stop("walk", PlayerIndex);
      AudioManager.Instance.Stop("dash", PlayerIndex);
      AudioManager.Instance.Play("falling", PlayerIndex);
      Debug.Log("Player " + PlayerIndex + " has fallen!");

      StartCoroutine(deathTeleport(RespawnPosition, true));
      return;
    }

    // Got hit by other player's damager collider OR got hit by food
    if (other.gameObject.layer == Utils.DamagerLayer && !_isInvulnerable && IsOnGround)
    {
      int perpetratorIndex = -1;

      if (other.gameObject.tag == "Player")
      {
        Movement theirScript = other.transform.parent.gameObject.GetComponent<Movement>();
        perpetratorIndex = theirScript.PlayerIndex;
        theirScript.PlayStab();
      }
      else
      {
        FoodShootBase itsScript = other.gameObject.GetComponent<FoodShootBase>();
        perpetratorIndex = itsScript.PlayerIndex;
      }

      Debug.Log("Player " + PlayerIndex + " got hit (debug count: " + _debugCount++ + ")!");

      if (KebabStack.Count != 0)
      {
        // Lost 1 hp

        AudioManager.Instance.Play("damaged", PlayerIndex);
        KebabStack.Pop(); // Popping first does the "-1" for KebabStack.Count

        // Disable all children
        for (int i = 0; i < _foodSliceTransforms[KebabStack.Count].transform.childCount; ++i)
        {
          Transform a = _foodSliceTransforms[KebabStack.Count].transform.GetChild(i);
          a.gameObject.SetActive(false);
        }

        Debug.Log("Player " + PlayerIndex + " has been hit and has this much health: " + KebabStack.Count);
      }
      else
      {
        // Died
        AudioManager.Instance.Play("die", PlayerIndex);
        StartCoroutine(deathTeleport(RespawnPosition, true));
        if (perpetratorIndex != -1 && perpetratorIndex != PlayerIndex)
        {
          DeathCountManager.Instance.IncrementKills(perpetratorIndex);
        }
      }

      startInvulnerability(_hitInvDuration);
      return;
    }
  }

  // Returns true if successful
  public bool AddFood(PooledObjectIndex index)
  {
    // Can't add food if full
    if (KebabStack.Count == _maxFood) return false;
    _stabParticles.Play();

    AudioManager.Instance.Play("pickup", PlayerIndex);
    Debug.Log("Player " + PlayerIndex + " added food. (Pooled object index: " + (int) index + ")"); 

    // Activate the correct position's correct child
    Transform sliceGameObject = _foodSliceTransforms[KebabStack.Count].GetChild((int) index);
    sliceGameObject.gameObject.SetActive(true);

    // Add to stack
    KebabStack.Push(index);

    return true;
  }

  public void PlayStab()
  {
    _stabParticles.Play();
  }

  public void ResetForMain()
  {
    // Player index was handled by MultiplayerManager
    
    // Position, rotation
    transform.position = RespawnPosition;
    Vector3 temp = new Vector3(RespawnPosition.x, 0, RespawnPosition.z);
    transform.rotation = Quaternion.LookRotation(-temp.normalized, Vector3.up);

    // Stuff to do with child game objects
    _damagerObject.SetActive(false);

    // Initialize some private stuff
    _stamina = _maxStamina;
    _minChargeDuration = _minChargeCost / _staminaCostRate;
    _currentMove = _rigidbody.velocity;
    _currentTurn = _rigidbody.rotation * Vector3.forward;

    emptyKebab();
  }

  // ================== Helpers
  
  private void updateIsOnGround()
  {
    // FIXME: magic numbers
    IsOnGround =  -0.5 < _rigidbody.position.y && _rigidbody.position.y < 3;
  }

  private void updateParticles()
  {
    if (IsOnGround)
    {
      _walkParticles.Play();
    }
    else
    {
      _walkParticles.Stop();
    }
    
    if (_currentMove.sqrMagnitude > 0.02f && IsOnGround)
    {
      AudioManager.Instance.Play("walk", PlayerIndex);
    }
    else
    {
      AudioManager.Instance.Stop("walk", PlayerIndex);
    }
  }

  private void move()
  {
    _idealMove = Utils.V2ToV3(_inputData.Move);

    _currentMove = Vector3.Lerp(
      _rigidbody.velocity,
      getAngleDependentSpeed() * _idealMove,
      _moveLerpRate);

    _currentMove.y = _rigidbody.velocity.y; // Avoid changing rigidbody's Y velocity
    _rigidbody.velocity = _currentMove;
  }

  private void turn()
  {
    if (_inputData.Turn.magnitude > 0)
    {
      // Turn with specific input
      _idealTurn = Utils.V2ToV3(_inputData.Turn);
    }
    else if (_idealMove.magnitude > 0) 
    {
      // Turn with movement
      _idealTurn = _idealMove;
    }
    else
    {
      // No inputs
      return;
    }
    
    _currentTurn = Vector3.Slerp(
      _currentTurn, 
      _idealTurn, 
      _turnSlerpRate);

    _rigidbody.rotation = Quaternion.LookRotation(_currentTurn);
  }

  private void regenStamina()
  {
    if (_canRegen && _stamina < _maxStamina)
    {
      _stamina += Time.deltaTime * _staminaRegenRate;
      _stamina = Mathf.Min(_stamina, _maxStamina);

    }
  }

  private float getAngleDependentSpeed()
  {
    float dot = Vector3.Dot(_idealMove, _currentTurn);

    if (dot < 0) return _strafeSpeedMultiplier * _moveSpeed;

    float speedMultiplier = Mathf.Lerp(
      Mathf.Pow(dot, 2),
      1,
      _strafeSpeedMultiplier);

    return speedMultiplier * _moveSpeed;
  }
  
  private IEnumerator charge()
  {
    // Start charge
    IsCharging = true;
    gameObject.layer = Utils.ChargerLayer;
    _damagerObject.SetActive(true);
    _canRegen = false;
    _rigidbody.velocity = _currentTurn * _chargeSpeed;
    _dashParticles.Play();
    _animator.SetBool("Charging", true);
    AudioManager.Instance.Stop("walk", PlayerIndex);
    AudioManager.Instance.Play("dash", PlayerIndex);

    // Enforce minimum
    _stamina -= _minChargeCost;
    yield return new WaitForSeconds(_minChargeDuration);

    // Continue charging
    while (_stamina > 0 && _inputData.Charge)
    {
      yield return null;
      _stamina -= Time.deltaTime * _staminaCostRate;
    }

    // Stop charge
    IsCharging = false;
    gameObject.layer = Utils.PlayerLayer;
    _damagerObject.SetActive(false);
    _dashParticles.Stop();
    _animator.SetBool("Charging", false);
    AudioManager.Instance.Stop("dash", PlayerIndex);
    if (_stamina <= 0)
    {
        AudioManager.Instance.Play("tired", PlayerIndex);
    }

    // Allow regen after some time
    yield return new WaitForSeconds(_regenDelay);
    _canRegen = true;
  }

  private IEnumerator shootFood()
  {
    // Can't shoot if no food
    if (KebabStack.Count == 0) yield break;

    // Play shooting effect
    AudioManager.Instance.Play("shoot", PlayerIndex);

    _isOnShootCoolDown = true;

    PooledObjectIndex foodType = KebabStack.Pop(); // Popping first does the "-1" for KebabStack.Count

    // Disable all children
    for (int i = 0; i < _foodSliceTransforms[KebabStack.Count].transform.childCount; ++i) {
      Transform a = _foodSliceTransforms[KebabStack.Count].transform.GetChild(i);
      a.gameObject.SetActive(false);
    }

    Transform tip = _foodSliceTransforms.Last(); //todo: change location
    GameObject bulletType = _foodBulletPrefab;
    switch (foodType)
    {
      case PooledObjectIndex.Null:     { throw new System.Exception();        }
      case PooledObjectIndex.Lamb:     { bulletType = _lambPrefab;     break; }
      case PooledObjectIndex.Onion:    { bulletType = _onionPrefab;    break; }
      case PooledObjectIndex.Tomato:   { bulletType = _tomatoPrefab;   break; }
      case PooledObjectIndex.Mushroom: { bulletType = _mushroomPrefab; break; }
      case PooledObjectIndex.Pepper:   { bulletType = _eggplantPrefab; break; }
      case PooledObjectIndex.Eggplant: { bulletType = _pepperPrefab;   break; }
    }    
    
    GameObject foodBullet = Instantiate(bulletType, tip.position, tip.rotation); // (low priority) TODO: maybe object pool
    foodBullet.GetComponent<FoodShootBase>().StartShot(tip, PlayerIndex);
    // Wait for cooldown
    yield return new WaitForSeconds(_shootCoolDown);
    _isOnShootCoolDown = false;
  }

  private void emptyKebab()
  {
    KebabStack = new Stack<PooledObjectIndex>();
    for (int j = 0; j < 3; ++j)
    {
      for (int i = 0; i < _foodSliceTransforms[j].transform.childCount; ++i)
      {
        Transform a = _foodSliceTransforms[j].transform.GetChild(i);
        a.gameObject.SetActive(false);
      }
    }
  }

  private void startInvulnerability(float time)
  {
    if (_currentInvInstance != null) StopCoroutine(_currentInvInstance);
    _currentInvInstance = invulnerable(time);
    StartCoroutine(_currentInvInstance);
  }

  private IEnumerator invulnerable(float time)
  {
    _isInvulnerable = true;
    Debug.Log("Player " + PlayerIndex + " has started invulnerability!");

    yield return new WaitForSeconds(time);

    _isInvulnerable = false;
    Debug.Log("Player " + PlayerIndex + " is no longer invulnerable!");
  }

  private IEnumerator deathTeleport(Vector3 respawnPosition, bool takesDamage)
  {
    while (KebabStack.Count > 0)
    {
      KebabStack.Pop();
      // Disable all children
      for (int i = 0; i < _foodSliceTransforms[KebabStack.Count].transform.childCount; ++i) {
        Transform a = _foodSliceTransforms[KebabStack.Count].transform.GetChild(i);
        a.gameObject.SetActive(false);
      }
    }

    Debug.Log("Player " + PlayerIndex + " has died!");
    DeathCountManager.Instance.IncrementDeath(PlayerIndex);
    Debug.Log("Player " + PlayerIndex + " is respawning...");

    // Start falling from the sky
    _rigidbody.position = respawnPosition; 
    _rigidbody.velocity = Vector3.zero;
    _rigidbody.useGravity = false;
    yield return new WaitForSeconds(_fallRespawnWaitDuration);
    _rigidbody.useGravity = true;

    // Restore stamina
    _stamina = _maxStamina;

    // Start invulnerability
    startInvulnerability(_fallInvDuration);
  }
}