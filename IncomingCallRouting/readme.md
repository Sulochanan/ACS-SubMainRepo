---
page_type: sample
languages:
- csharp
products:
- azure
- azure-communication-services
---


# Incoming call routing Sample

This is a sample web app service, shows how the Azure Communication Services Calling server SDK (used to build IVR related solutions). This sample receives an incoming call request whenever a call made to a communication identifier (IVR Participant) then API first answers the call and plays an audio message. If the callee selects an option for transferring the call, then the application transfer a call to the target participant and IVR participant then leaves the call. If the callee selects any other option then the application disconnect the call.
The application is an app service application built on .Net Framework 4.8.

## Prerequisites

- Create an Azure account with an active subscription. For details, see [Create an account for free](https://azure.microsoft.com/free/)
- [Visual Studio (2019 and above)](https://visualstudio.microsoft.com/vs/)
- [.NET Framework 4.8](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48) (Make sure to install version that corresponds with your visual studio instance, 32 vs 64 bit)
- Create an Azure Communication Services resource. For details, see [Create an Azure Communication Resource](https://docs.microsoft.com/azure/communication-services/quickstarts/create-communication-resource). You'll need to record your resource **connection string** for this sample.
- [Configuring the webhook](https://docs.microsoft.com/en-us/azure/devops/service-hooks/services/webhooks?view=azure-devops) for **Microsoft.Communication.IncomingCall** event.
- (Optional) Create Azure Speech resource for generating custom message to be played by application. Follow [here](https://docs.microsoft.com/azure/cognitive-services/speech-service/overview#try-the-speech-service-for-free) to create the resource.

> Note: the samples make use of the Microsoft Cognitive Services Speech SDK. By downloading the Microsoft Cognitive Services Speech SDK, you acknowledge its license, see [Speech SDK license agreement](https://aka.ms/csspeech/license201809).

## Before running the sample for the first time

1. Open an instance of PowerShell, Windows Terminal, Command Prompt or equivalent and navigate to the directory that you'd like to clone the sample to.
2. git clone https://github.com/Azure-Samples/Communication-Services-dotnet-quickstarts.git.

### Locally deploying the sample app

1. Go to IncomingCallRouting folder and open `IncomingCallRouting.sln` solution in Visual Studio
2. Open the appsetting.json file to configure the following settings

	- ResourceConnectionString: Azure Communication Service resource's connection string.
	- AppCallBackUri: URI of the deployed app service
	- AudioFileUri: public url of wav audio file
	- TargetParticipant: ACS resource ID of target participant to transfer the call.
	- SecretValue: Query string for callback URL
	- IVRParticipant: ACS resource ID or "*" for accepting incoming calls from all the ACS user IDs.

3. Run `IncomingCallRouting` project.
4. Use postman or any debugging tool and open url - https://localhost:5001

### Publish to Azure

1. Right click the `IncomingCallRouting` project and select Publish.
2. Create a new publish profile and select your app name, Azure subscription, resource group and etc.
3. After publishing, add the following configurations on azure portal (under app service's configuration section).

	- ResourceConnectionString: Azure Communication Service resource's connection string.
	- AppCallBackUri: URI of the deployed app service.
	- AudioFileUri: public url of wav audio file.
	- TargetParticipant: ACS resource ID of target participant to transfer the call.
	- SecretValue: Query string for callback URL.
	- IVRParticipant: ACS resource ID or "*" for accepting incoming calls from all the ACS user IDs.
	```
	For e.g. 8:acs:ab12b0ea-85ea-4f83-b0b6-84d90209c7c4_00000009-bce0-da09-54b7-xxxxxxxxxxxx)
	```


4. Detailed instructions on publishing the app to Azure are available at [Publish a Web app](https://docs.microsoft.com/visualstudio/deployment/quickstart-deploy-to-azure?view=vs-2019).

5. After publishing you application register webhook to your ACS resource using armclient.
```
armclient put "/subscriptions/<subscription id>/resourceGroups/<rg>/providers/Microsoft.Communication/CommunicationServices/<acs name>/providers/Microsoft.EventGrid/eventSubscriptions/IncomingCallEventSub?api-version=2020-06-01" "{'properties':{'destination':{'properties':{'endpointUrl':'https://<deployed-web-app-url>/OnIncomingCall'},'endpointType':'WebHook'},'filter':{'includedEventTypes': ['Microsoft.Communication.IncomingCall']}}}" -verbose
```


### How to test

1. Refer [web calling sample](https://docs.microsoft.com/en-us/azure/communication-services/samples/web-calling-sample) sample for creating 3 ACS User Identities.

2. Put identity of User2 and user3 in the configuration as `IVRParticipant` and `TargetParticipant` respectively, (identities are in this form `8:acs:<ACS resource id>-<guid>`)

3. Make a call from User1 to User2


**Note**: While you may use http://localhost for local testing, the sample when deployed will only work when served over https. The SDK [does not support http](https://docs.microsoft.com/azure/communication-services/concepts/voice-video-calling/calling-sdk-features#user-webrtc-over-https).

### Troubleshooting

1. Solution doesn\'t build, it throws errors during build

	Clean/rebuild the C# solution
