﻿using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using Unity.Collections;
using VRC.SDKBase;
using VRC.Udon;

namespace OpenFlightVRC
{
	//This chunk of code allows the OpenFlight version number to be set automatically from the package.json file
	//its done using this method for dumb unity reasons but it works so whatever
#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;


public class OpenFlightScenePostProcessor {
	[PostProcessSceneAttribute]
	public static void OnPostProcessScene() {
		//get the openflight version from the package.json file
		string packageJson = System.IO.File.ReadAllText("Packages/com.mattshark.openflight/package.json");
		string version = packageJson.Split(new string[] { "\"version\": \"" }, System.StringSplitOptions.None)[1].Split('"')[0];
		//find all the OpenFlight scripts in the scene
		OpenFlight[] openFlightScripts = Object.FindObjectsOfType<OpenFlight>();
		foreach (OpenFlight openFlightScript in openFlightScripts)
		{
			//set their version number
			openFlightScript.OpenFlightVersion = version;
		}
	}
}
#endif
	public class OpenFlight : UdonSharpBehaviour
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

		/// <summary>
		/// The current flight mode
		/// </summary>
		public string flightMode = "Auto";
		string flightModePrevious = "Auto";
		private VRCPlayerApi LocalPlayer;

		/// <summary>
		/// If true, the player is allowed to fly
		/// </summary>
		[ReadOnly]
		public bool flightAllowed = false;
		public bool flightForcedOff = false; //used for external scripts to force flight off no matter what

		/// <summary>
		/// Turns flight off
		/// </summary>
		void SwitchFlight()
		{
			wingedFlight.SetActive(false);
			flightAllowed = false;
		}

		public void Start()
		{
			LocalPlayer = Networking.LocalPlayer;
			if (!LocalPlayer.IsUserInVR())
			{
				FlightOff();
			}
		}

		/// <summary>
		/// Enables flight if the player is in VR
		/// </summary>
		public void FlightOn()
		{
			if (LocalPlayer.IsUserInVR() && !flightForcedOff)
			{
				SwitchFlight();
				wingedFlight.SetActive(true);
				flightMode = "On";
				flightAllowed = true;
			}
		}

		/// <summary>
		/// Disables flight
		/// </summary>
		public void FlightOff()
		{
			if (!flightForcedOff)
			{
				SwitchFlight();
				wingedFlight.SetActive(false);
				flightMode = "Off";
				flightAllowed = false;
			}
		}

		/// <summary>
		/// Allows the avatar detection system to control if the player can fly or not
		/// </summary>
		public void FlightAuto()
		{
			if (LocalPlayer.IsUserInVR() && !flightForcedOff)
			{
				flightMode = "Auto";
				flightAllowed = false;

				//tell the avatar detection script to check if the player can fly again
				if (avatarDetection != null)
				{
					avatarDetection.ReevaluateFlight();
				}
			}
		}

		/// <summary>
		/// Allows flight if flightMode is set to Auto
		/// </summary>
		/// <seealso cref="FlightAuto"/>
		public void CanFly()
		{
			if (string.Equals(flightMode, "Auto") && !flightForcedOff)
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
			if (string.Equals(flightMode, "Auto") && !flightForcedOff)
			{
				SwitchFlight();
				wingedFlight.SetActive(false);
				flightAllowed = false;
			}
		}

		//These are used by scripts that need to force flight on or off no matter what the player wants
		public void ForceDisableFlight()
		{
			SwitchFlight();
			wingedFlight.SetActive(false);
			flightModePrevious = flightMode;
			flightMode = "Forced Off";
			flightAllowed = false;
			flightForcedOff = true;
		}

		public void ResetForcedFlight()
		{
			flightForcedOff = false;
			flightMode = flightModePrevious;
			if (string.Equals(flightMode, "Auto"))
			{
				FlightAuto();
			}

			if (string.Equals(flightMode, "On"))
			{
				FlightOn();
			}

			if (string.Equals(flightMode, "Off"))
			{
				FlightOff();
			}
		}
	}
}
