# Mike the Pedagogical Agent

Welcome to the official repository of Mike The pedagogical agent v2.0 now powered by LLMs.

<img src="./img/agent.gif" width="20%"/>

This is a Unity3D project which was originally created for a CentraleSupelec Lecture on Artificial Intelligence and Social Sciences.

This project contains the foundation of a Socially Interactive Agent or Embodied Conversational Agent.
It is greatly inspired by other projects such as Greta, Marc and the Virtual Human Toolkit.
It was not really designed to fully reproduce the capacity of a SIA, but to offer an easy way for students to discover how an agent can work.

Previous versions (available on the repository) used Mike Alger's 3D model but actual version uses Avaturn 3D Models.

# Installation

You can clone the current project using your favorite Git Client or you can download it as a zip archive and extract it on your drive.

Mike the PA should work on Linux, Windows and MacOS versions of Unity3D starting from version 6000.0.60f1.
On Windows, it uses by default the Windows Text-To-Speech and Speech-To-Text. On MacOS and Linux, you have to use Macoron's Whisper plugin and Piper-TTS (these can be used on Windows as well).

As a Unity3D project, you need to add the project in your project's list using UnityHub "Add Project" functionality.

# Usage

The project should open on a blank scene. Open the CS_Scene to load the default scene. If you are familiar with Unity3D, you should get around quite easily.

The scene only has a fake background, a Camera, some lighting and our agent. The agent is a GameObject equipped with custom scripts in order to allow him to :
- Run a dialog using an external LLM, UI Buttons are used to start and stop recording your voice.  
- Follow an object with its gaze
- Perform some pre-rendered animations (downloaded from Mixamo)
- Do basic lip animation mixed with facial expressions
- Receive MediaPipe's blendshapes from a WebSocket and play them on the agent

The project is quite light and straightforward. It does not compete with other exising agent plaforms such as [Greta](https://github.com/isir/greta), Marc or [VHTK](https://vhtoolkit.ict.usc.edu/) but can be a good playground to manipulate an interactive character.

## The real-time voice (LLM and Whisper) dialog manager
Now, MikeTPA 2.0 uses a AvaturnLLMDialogManager class which is implemented to serve as a demo for integrating modern AI solutions for Speech-To-Text and LLMs. On Windows, it uses by default the Windows Speech API but it can use Macoron plugin [WHISPER.Unity](https://github.com/Macoron/whisper.unity) as well. The resulting dialog can be quite slow and imprecise, depending on you connection and the size of the LLM you are using, but it can be used as a starting point to integrate online, more precise and faster solutions. Please note that the model for the Whisper plugin is not distributed here and should be retrieved by following Macoron's instructions on his respective repositories.

# Credits

- Original 3D Model from [Mike Alger](https://mikealger.com/portfolio/avatar#top), prerendered animations from [Mixamo](https://www.mixamo.com), current 3D models from Avaturn.
- Thanks to [Julien Saunier](https://pagesperso.litislab.fr/~jsaunier/) for the OpenMary integration. 
- Emotion recognition from [Omar Ayman](https://github.com/otaha178/Emotion-recognition)
- WebSocket implementation from [STA](https://github.com/sta/websocket-sharp)
- WindowsTTS wrapper adapted from [Chad Weisshaar](https://chadweisshaar.com/blog/2015/07/02/microsoft-speech-for-unity/) and [Jinky Jung](https://github.com/VirtualityForSafety/UnityWindowsTTS)


If you use Mike the PA in one of your projects, citing this repository is appreciated.

Copyright. Brian Ravenet