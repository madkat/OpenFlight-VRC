﻿/**
 * @ Maintainer: Mattshark89
 */

using UdonSharp;
using UnityEngine;
using Unity.Collections;
using VRC.SDKBase;

namespace OpenFlightVRC
{
    using UnityEditor;
    //This chunk of code allows the OpenFlight version number to be set automatically from the package.json file
    //its done using this method for dumb unity reasons but it works so whatever
#if !COMPILER_UDONSHARP && UNITY_EDITOR
    using UnityEditor.Callbacks;

    using VRC.SDKBase.Editor.BuildPipeline;

    public class OpenFlightScenePostProcessor
	{
		[PostProcessScene]
		public static void OnPostProcessScene()
		{
			//get the path of this script asset
			string guid = AssetDatabase.FindAssets(string.Format("t:Script {0}", typeof(OpenFlight).Name))[0];
			string path = AssetDatabase.GUIDToAssetPath(guid);

			//get the openflight package info
			UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);

			//find all the OpenFlight scripts in the scene
			OpenFlight[] openFlightScripts = Object.FindObjectsOfType<OpenFlight>();
			foreach (OpenFlight openFlightScript in openFlightScripts)
			{
				//set their version number
				openFlightScript.OpenFlightVersion = packageInfo.version;
			}
		}
	}

	public class OpenFlightChecker : VRC.SDKBase.Editor.BuildPipeline.IVRCSDKBuildRequestedCallback
	{
        public int callbackOrder => 0;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            //check to make sure the world scale of openflight is 1
			OpenFlight[] openFlightScripts = Object.FindObjectsOfType<OpenFlight>();

			foreach (OpenFlight openFlightScript in openFlightScripts)
			{
				if (openFlightScript.transform.lossyScale != Vector3.one)
				{
					//show a popup
					EditorUtility.DisplayDialog("OpenFlight World Scale Error", "The world scale of the OpenFlight object must be 1.0. Please reset the scale of the OpenFlight object to 1.0.", "OK");

					Debug.LogError("OpenFlight: The world scale of the OpenFlight object must be 1.0. Please reset the scale of the OpenFlight object to 1.0.", openFlightScript);
					return false;
				}
			}

			return true;
        }
	}
#endif

public enum FlightMode
	{
		Off,
		Auto,
		On,
		ForcedOff
	}

	[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
	public class OpenFlight : LoggableUdonSharpBehaviour
	{
		//this removes any override that the editor might have set through the inspector ([HideInInspector] does NOT do that)
		/// <summary>
		/// The version of OpenFlight that is currently installed in the world. This should not be set, as this value is set upon scene load
		/// </summary>
		[System.NonSerialized]
		public string OpenFlightVersion = "?.?.?";

		/// <summary>
		/// 	The WingedFlight game object, used to enable/disable the WingedFlight script
		/// </summary>
		public GameObject wingedFlight;

		/// <summary>
		/// The AvatarDetection script, used to re-evaluate flight upon switching to auto
		/// </summary>
		public AvatarDetection avatarDetection;

		/// <inheritdoc cref="flightMode"/>
		[FieldChangeCallback(nameof(flightMode)), SerializeField]
		private FlightMode _flightMode = FlightMode.Auto;
		/// <summary>
		/// The current flight mode.
		/// </summary>
		public FlightMode flightMode
		{
			get => _flightMode;
			set
			{
				_flightMode = value;
				//update the flight mode string
				switch (value)
				{
					case FlightMode.Off:
						flightModeString = "Off";
						break;
					case FlightMode.Auto:
						flightModeString = "Auto";
						break;
					case FlightMode.On:
						flightModeString = "On";
						break;
					case FlightMode.ForcedOff:
						flightModeString = "Forced Off";
					default:
						flightModeString = "Unknown";
						break;
				}
			}
		}

		/// <summary>
		/// The current flight mode as a string.
		/// </summary>
		[ReadOnlyInspector]
		public string flightModeString = "";

		/// <summary>
		/// The previous flight mode
		/// </summary>
		public FlightMode previousFlightMode = FlightMode.Auto;

		private VRCPlayerApi _localPlayer;

		/// <inheritdoc cref="flightAllowed"/>
		[ReadOnly, ReadOnlyInspector, FieldChangeCallback(nameof(flightAllowed))]
		private bool _flightAllowed = false;
		/// <summary>
		/// If true, the player is allowed to fly
		/// </summary>
		public bool flightAllowed
		{
			get => _flightAllowed;
			set
			{
				_flightAllowed = value;

				//update the flight allowed string
				if (value)
				{
					flightAllowedString = "Active";
				}
				else
				{
					if (InVR())
					{
						flightAllowedString = "Inactive";
					}
					else
					{
						flightAllowedString = "Inactive (Not in VR)";
					}
				}
			}
		}

		[ReadOnlyInspector]
		public string flightAllowedString = "";

		/// <summary>
		/// If true, flight has been forced off by a script
		/// </summary>
		public bool flightForcedOff = false;

		/// <summary>
		/// If true, the system will ignore the VR check and allow flight even if the player is not in VR
		/// </summary>
		/// <remarks>
		/// You REALLY should not turn this on. This is purely for testing purposes
		/// </remarks>
		[ReadOnlyInspector]
		public bool ignoreVRCheck = false;

		/// <summary>
		/// Turns flight off
		/// </summary>
		void SwitchFlight()
		{
			wingedFlight.SetActive(false);
			flightAllowed = false;
		}

		/// <summary>
		/// Checks if the player is in VR
		/// </summary>
		/// <returns></returns>
		private bool InVR()
		{
			if (ignoreVRCheck)
			{
				Logger.LogWarning("VR check is being ignored! This should not be enabled in a production build!", this);
			}
			
			//ensure the user is valid
			if (_localPlayer == null)
			{
				_localPlayer = Networking.LocalPlayer;
			}

			return _localPlayer.IsUserInVR() || ignoreVRCheck;
		}

		public void Start()
		{
			//update the flight mode string by setting the flight mode to itself to trigger the property setter
			flightMode = _flightMode;
			//update the flight allowed string aswell
			flightAllowed = _flightAllowed;

			_localPlayer = Networking.LocalPlayer;
			if (!InVR())
			{
				FlightOff();
			}

			//apply flight mode
			ApplyFlightMode();

			Logger.Log("OpenFlight version " + OpenFlightVersion, this);
		}

		/// <summary>
		/// Call helper method associated with current flightMode.
		/// </summary>
		private void ApplyFlightMode()
		{
			switch (flightMode)
			{
				case FlightMode.On:
					FlightOn();
					break;
				case FlightMode.Off:
					FlightOff();
					break;
				case FlightMode.Auto:
					FlightAuto();
					break;
				case FlightMode.ForcedOff:
					break;
				default:
					Logger.LogWarning("Invalid flight mode: " + flightModeString, this);
					break;
			}
		}

		/// <summary>
		/// Enables flight if the player is in VR
		/// </summary>
		public void FlightOn()
		{
			if (InVR())
			{
				SwitchFlight();
				wingedFlight.SetActive(true);
				flightMode = FlightMode.On;
				flightAllowed = true;
				Logger.Log("Flight turned on", this);
			}
			else
			{
				Logger.Log("Flight cannot be turned on because the player is not in VR", this);
			}
		}

		/// <summary>
		/// Disables flight
		/// </summary>
		public void FlightOff()
		{
			SwitchFlight();
			wingedFlight.SetActive(false);
			flightMode = FlightMode.Off;
			flightAllowed = false;
			Logger.Log("Flight turned off", this);
		}

		/// <summary>
		/// Allows the avatar detection system to control if the player can fly or not
		/// </summary>
		public void FlightAuto()
		{
			if (InVR())
			{
				flightMode = FlightMode.Auto;
				flightAllowed = false;

				//tell the avatar detection script to check if the player can fly again
				if (avatarDetection != null)
				{
					avatarDetection.ReevaluateFlight();
				}
				Logger.Log("Flight set to auto", this);
			}
			else
			{
				Logger.Log("Flight cannot be set to auto because the player is not in VR", this);
			}
		}

		/// <summary>
		/// Allows flight if flightMode is set to Auto
		/// </summary>
		/// <seealso cref="FlightAuto"/>
		public void CanFly()
		{
			if (flightMode == FlightMode.Auto)
			{
				SwitchFlight();
				wingedFlight.SetActive(true);
				flightAllowed = true;
			}
		}

		/// <summary>
		/// Disables flight if flightMode is set to Auto
		/// </summary>
		public void CannotFly()
		{
			if (flightMode == FlightMode.Auto)
			{
				SwitchFlight();
				wingedFlight.SetActive(false);
				flightAllowed = false;
			}
		}

		/// <summary>
		/// Disable flight, by script, until re-enabled
		/// </summary>
		public void ForceDisableFlight()
		{
			if (flightMode != FlightMode.ForcedOff)
			{
				flightForcedOff = true;
				previousFlightMode = flightMode;
				FlightOff();
				flightMode = FlightMode.ForcedOff;
			}
		}

		/// <summary>
		/// Remove forced disable status from flight
		/// </summary>
		public void ReEnableFlight()
		{
			flightForcedOff = false;
			flightMode = previousFlightMode;
			ApplyFlightMode();
		}
	}
}
