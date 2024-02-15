using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A simple class to redirect Console.WriteLine to Unity's Console:
public class Console 
{
	public static void WriteLine(string str, params object[] args) {
		var s = string.Format(str, args);
		Debug.Log(s);
	}
}
