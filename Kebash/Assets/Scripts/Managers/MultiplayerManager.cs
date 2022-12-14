using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class MultiplayerManager : MonoBehaviour
{
  public static MultiplayerManager Instance;

  private float _spawnYOffset = 25;

  // ================== Accessors

  public PlayerInputManager PlayerInputManager { get; private set; }

  public List<Movement> PlayerScripts { get; private set; } = new List<Movement>();

  public int PlayerCount { get { return PlayerScripts.Count; } }

  // ================== Methods

  void Awake()
  { 
    Instance = this;
    PlayerInputManager = gameObject.GetComponent<PlayerInputManager>();
  }

  public Vector3 GetAveragePlayerPositionForCamera()
  {
    // Player contributions are bounded to screen space
    Vector3 averagePosition = Vector3.zero;

    if (PlayerCount == 0) return averagePosition;

    for (int i = 0; i < PlayerCount; ++i)
    {
      Vector3 clampedPosition = GetPlayerPosition(i);
      clampedPosition.x = Mathf.Clamp(clampedPosition.x, -13.75f, 13.75f);
      clampedPosition.z = Mathf.Clamp(clampedPosition.z,  -8.75f,  8.75f);
      averagePosition += clampedPosition;
    }

    return averagePosition / PlayerCount;
  }

  public Vector3 GetPlayerPosition(int playerIndex)
  {
    if (playerIndex > PlayerCount) throw new System.Exception("No such player");

    return PlayerScripts[playerIndex].gameObject.transform.position;
  }

  public Vector3 GetPlayerSpawnPosition(int playerIndex)
  {
    switch (PlayerCount)
    {
      case 0: { return new Vector3(0, _spawnYOffset, 0); }
      case 1: { return new Vector3(0, _spawnYOffset, 0); }
      case 2:
      {
        switch (playerIndex)
        {
          case 0:  { return new Vector3(-10, _spawnYOffset,  6); }
          default: { return new Vector3( 10, _spawnYOffset, -6); }
        }
      }
      case 3:
      {
        switch (playerIndex)
        {
          case 0:  { return new Vector3( 0,  _spawnYOffset,  6); }
          case 1:  { return new Vector3(-10, _spawnYOffset, -3); }
          default: { return new Vector3( 10, _spawnYOffset, -3); }
        }
      }
      case 4:
      {
        switch (playerIndex)
        {
          case 0:  { return new Vector3(-10, _spawnYOffset,  6); }
          case 1:  { return new Vector3(-10, _spawnYOffset, -6); }
          case 2:  { return new Vector3( 10, _spawnYOffset,  6); }
          default: { return new Vector3( 10, _spawnYOffset, -6); }
        }
      }
    }
    throw new System.Exception("Unexpected number of players");
  }

  public void OnPlayerJoined(PlayerInput playerInput)
  {
    Movement playerScript = playerInput.gameObject.GetComponent<Movement>();
    PlayerScripts.Add(playerScript);

    ReinitializeAllPlayers();

    Debug.Log("Player has joined.");

    if (PlayerCount == 4) PlayerInputManager.DisableJoining();
  }
  
  public void OnPlayerLeft(PlayerInput playerInput)
  {
    Movement playerScript = playerInput.gameObject.GetComponent<Movement>();
    PlayerScripts.Remove(playerScript);
    
    ReinitializeAllPlayers();

    Debug.Log("Player has left.");
  }

  public void ReinitializeAllPlayers()
  {
    for (int i = 0; i < PlayerCount; ++i)
    {
      Movement playerScript = PlayerScripts[i];

      playerScript.PlayerIndex = i;

      Vector3 spawnPosition = GetPlayerSpawnPosition(i);
      playerScript.RespawnPosition = spawnPosition;

      if (GameStateManager.Instance.State == GameState.Menu)
      {
        playerScript.ResetForMain();
      }
    }
  }
}
