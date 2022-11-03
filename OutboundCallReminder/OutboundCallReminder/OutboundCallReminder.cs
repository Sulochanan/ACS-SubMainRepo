// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Communication.Server.Calling.Sample.OutboundCallReminder
{
    using Azure;
    using Azure.Communication;
    using Azure.Communication.CallAutomation;
    using Azure.Communication.Identity;
    using Microsoft.CognitiveServices.Speech.Transcription;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.ComponentModel.Composition.Primitives;
    using System.Configuration;
    using System.Linq;
    using System.Net.NetworkInformation;
    using System.Runtime.CompilerServices;
    using System.Runtime.Remoting.Metadata.W3cXsd2001;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.UI.WebControls;
    using System.Xml;


    public class OutboundCallReminder
    {
        private CallConfiguration callConfiguration;
        private CallAutomationClient callClient;
        private CallConnection callConnection;
        private CancellationTokenSource reportCancellationTokenSource;
        private CancellationToken reportCancellationToken;


        private TaskCompletionSource<bool> callEstablishedTask;
        private TaskCompletionSource<bool> playAudioCompletedTask;
        private TaskCompletionSource<bool> callTerminatedTask;
        private TaskCompletionSource<bool> toneReceivedCompleteTask;
        private TaskCompletionSource<bool> addParticipantCompleteTask;
        private readonly int maxRetryAttemptCount = Convert.ToInt32(ConfigurationManager.AppSettings["MaxRetryCount"]);

        public OutboundCallReminder(CallConfiguration callConfiguration)
        {
            this.callConfiguration = callConfiguration;
            callClient = new CallAutomationClient(this.callConfiguration.ConnectionString);
        }

        public async Task Report(string targetPhoneNumber, string participant)
        {
            reportCancellationTokenSource = new CancellationTokenSource();
            reportCancellationToken = reportCancellationTokenSource.Token;

            try
            {
                callConnection = await CreateCallAsync(targetPhoneNumber).ConfigureAwait(false);
                RegisterToDtmfResultEvent(callConnection.CallConnectionId);

                await StartRecognizingDtmf(targetPhoneNumber).ConfigureAwait(false);
                var playAudioCompleted = await playAudioCompletedTask.Task.ConfigureAwait(false);

                if (!playAudioCompleted)
                {
                    await HangupAsync().ConfigureAwait(false);
                }
                else
                {
                    var toneReceivedComplete = await toneReceivedCompleteTask.Task.ConfigureAwait(false);
                    if (toneReceivedComplete)
                    {
                        Logger.LogMessage(Logger.MessageType.INFORMATION, $"Initiating add participant from number {targetPhoneNumber} and participant identifier is {participant}");
                        var addParticipantCompleted = await AddParticipant(participant);
                        if (!addParticipantCompleted)
                        {
                            await RetryAddParticipantAsync(async () => await AddParticipant(participant));
                        }
                    }

                    await HangupAsync().ConfigureAwait(false);
                }

                // Wait for the call to terminate
                await callTerminatedTask.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogMessage(Logger.MessageType.ERROR, $"Call ended unexpectedly, reason: {ex.Message}");
            }
        }

        private async Task RetryAddParticipantAsync(Func<Task<bool>> action)
        {
            int retryAttemptCount = 1;
            while (retryAttemptCount <= maxRetryAttemptCount)
            {
                Logger.LogMessage(Logger.MessageType.INFORMATION, $"Retrying add participant attempt {retryAttemptCount} is in progress");
                var addParticipantResult = await action();
                if (addParticipantResult)
                {
                    return;
                }
                else
                {
                    Logger.LogMessage(Logger.MessageType.INFORMATION, $"Retry add participant attempt {retryAttemptCount} has failed");
                    retryAttemptCount++;
                }
            }
        }

        private async Task<CallConnection> CreateCallAsync(string targetPhoneNumber)
        {
            try
            {
                //Preparting request data
                CallSource source = new CallSource(new CommunicationUserIdentifier(callConfiguration.SourceIdentity));
                source.CallerId = new PhoneNumberIdentifier(callConfiguration.SourcePhoneNumber);
                var target = new PhoneNumberIdentifier(targetPhoneNumber);
                var createCallOption = new CreateCallOptions(source,
                    new List<CommunicationIdentifier>() { target },
                    new Uri(callConfiguration.AppCallbackUrl)
                     );

                Logger.LogMessage(Logger.MessageType.INFORMATION, "Performing CreateCall operation");
                var call = await callClient.CreateCallAsync(createCallOption).ConfigureAwait(false);

                Logger.LogMessage(Logger.MessageType.INFORMATION, $"CreateCallConnectionAsync response --> {call.GetRawResponse()}, Call Connection Id: {call.Value.CallConnectionProperties.CallConnectionId}");

                Logger.LogMessage(Logger.MessageType.INFORMATION, $"Call initiated with Call Connection id: {call.Value.CallConnectionProperties.CallConnectionId}");

                RegisterToCallStateChangeEvent(call.Value.CallConnectionProperties.CallConnectionId);

                //Wait for operation to complete
                await callEstablishedTask.Task.ConfigureAwait(false);

                return call.Value.CallConnection;
            }
            catch (Exception ex)
            {
                Logger.LogMessage(Logger.MessageType.ERROR, string.Format("Failure occured while creating/establishing the call. Exception: {0}", ex.Message));
                throw ex;
            }
        }

        private async Task PlayAudioAsync()
        {
            if (reportCancellationToken.IsCancellationRequested)
            {
                Logger.LogMessage(Logger.MessageType.INFORMATION, "Cancellation request, PlayAudio will not be performed");
                return;
            }

            try
            {
                // Preparing data for request
                var playAudioRequest = new PlayOptions()
                {

                    OperationContext = Guid.NewGuid().ToString(),
                    Loop = true,
                };
                PlaySource AudioFileUri = new FileSource(new Uri(callConfiguration.AudioFileUrl));

                Logger.LogMessage(Logger.MessageType.INFORMATION, "Performing PlayAudio operation");
                var response = await callConnection.GetCallMedia().PlayToAllAsync(AudioFileUri, playAudioRequest, reportCancellationToken).ConfigureAwait(false);

                Logger.LogMessage(Logger.MessageType.INFORMATION, $"PlayAudioAsync response --> {response}, Id: {response.ClientRequestId}, Status: {response.Status}, OperationContext: {response.Content}, ResultInfo: {response.ContentStream}");

                if (response.Status == 202)
                {
                    Logger.LogMessage(Logger.MessageType.INFORMATION, $"Play Audio state: {response}");
                    // listen to play audio events
                    RegisterToPlayAudioResultEvent(playAudioRequest.OperationContext);
                    var completedTask = await Task.WhenAny(playAudioCompletedTask.Task, Task.Delay(30 * 1000)).ConfigureAwait(false);

                    if (completedTask != playAudioCompletedTask.Task)
                    {
                        Logger.LogMessage(Logger.MessageType.INFORMATION, "No response from user in 30 sec, initiating hangup");
                        playAudioCompletedTask.TrySetResult(false);
                        toneReceivedCompleteTask.TrySetResult(false);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Logger.LogMessage(Logger.MessageType.ERROR, "Play audio operation cancelled");
            }
            catch (Exception ex)
            {
                Logger.LogMessage(Logger.MessageType.ERROR, $"Failure occured while playing audio on the call. Exception: {ex.Message}");
            }
        }

        private async Task StartRecognizingDtmf(string targetPhoneNumber)
        {
            if (reportCancellationToken.IsCancellationRequested)
            {
                Logger.LogMessage(Logger.MessageType.INFORMATION, "Cancellation request, PlayAudio will not be performed");
                return;
            }

            try
            {
                string audioFilePath = callConfiguration.AudioFileUrl;
                // string audioFilePath = callConfiguration.AudioFileUrl + CallConfiguration.AudioFileName;
                PlaySource audioFileUri = new FileSource(new Uri(audioFilePath));

                // listen to play audio events
                RegisterToPlayAudioResultEvent(callConnection.CallConnectionId);

                //Start recognizing Dtmf Tone
                var recognizeOptions = new CallMediaRecognizeDtmfOptions(new PhoneNumberIdentifier(targetPhoneNumber), 1);
                recognizeOptions.InterToneTimeout = TimeSpan.FromSeconds(5);
                recognizeOptions.InitialSilenceTimeout = TimeSpan.FromSeconds(30);
                recognizeOptions.InterruptPrompt = true;
                recognizeOptions.InterruptCallMediaOperation = true;
                recognizeOptions.Prompt = audioFileUri;
                recognizeOptions.StopTones = new List<DtmfTone> { DtmfTone.Pound };

                var resp = await callConnection.GetCallMedia().StartRecognizingAsync(recognizeOptions, reportCancellationToken);

                Logger.LogMessage(Logger.MessageType.INFORMATION, $"StartRecognizingAsync response --> " +
                $"{resp}, Id: {resp.ClientRequestId}, Status: {resp.Status}");

                //Wait for 30 secs for input
                var completedTask = await Task.WhenAny(playAudioCompletedTask.Task, Task.Delay(30 * 1000)).ConfigureAwait(false);

                if (completedTask != playAudioCompletedTask.Task)
                {
                    Logger.LogMessage(Logger.MessageType.INFORMATION, "No response from user in 30 sec, initiating hangup");
                    playAudioCompletedTask.TrySetResult(false);
                    toneReceivedCompleteTask.TrySetResult(false);
                }
            }
            catch (TaskCanceledException)
            {
                Logger.LogMessage(Logger.MessageType.ERROR, " Start recognizing with Play audio prompt for Custom message got cancelled");
            }
            catch (Exception ex)
            {
                Logger.LogMessage(Logger.MessageType.ERROR, $"Failure occured while start recognizing with Play audio prompt. Exception: {ex.Message}");
            }
        }
        private async Task HangupAsync()
        {
            if (reportCancellationToken.IsCancellationRequested)
            {
                Logger.LogMessage(Logger.MessageType.INFORMATION, "Cancellation request, Hangup will not be performed");
                return;
            }

            Logger.LogMessage(Logger.MessageType.INFORMATION, "Performing Hangup operation");
            var hangupResponse = await callConnection.HangUpAsync(true, reportCancellationToken).ConfigureAwait(false);

            Logger.LogMessage(Logger.MessageType.INFORMATION, $"HangupAsync response --> {hangupResponse}");

        }

        private async Task CancelAllMediaOperations()
        {
            if (reportCancellationToken.IsCancellationRequested)
            {
                Logger.LogMessage(Logger.MessageType.INFORMATION, "Cancellation request, CancelMediaProcessing will not be performed");
                return;
            }

            Logger.LogMessage(Logger.MessageType.INFORMATION, "Performing cancel media processing operation to stop playing audio");

            var operationContext = Guid.NewGuid().ToString();
            var response = await callConnection.GetCallMedia().CancelAllMediaOperationsAsync(reportCancellationToken).ConfigureAwait(false);

            Logger.LogMessage(Logger.MessageType.INFORMATION, $"PlayAudioAsync response --> {response}, Id: {response.ClientRequestId}, Status: {response.Status}, OperationContext: {response.Content}, ResultInfo: {response.ContentStream}");
        }

        private void RegisterToCallStateChangeEvent(string callConnectionId)
        {
            callEstablishedTask = new TaskCompletionSource<bool>(TaskContinuationOptions.RunContinuationsAsynchronously);
            reportCancellationToken.Register(() => callEstablishedTask.TrySetCanceled());

            callTerminatedTask = new TaskCompletionSource<bool>(TaskContinuationOptions.RunContinuationsAsynchronously);
            var callConnectedNotificaiton = new NotificationCallback((callEvent) =>
            {
                Logger.LogMessage(Logger.MessageType.INFORMATION, $"Call State changed to Connected");
                EventDispatcher.Instance.Unsubscribe("CallConnected", callConnectionId);
                callEstablishedTask.TrySetResult(true);
            });

            //Set the callback method for call Disconnected
            var callDisconnectedNotificaiton = new NotificationCallback((callEvent) =>
            {
                Logger.LogMessage(Logger.MessageType.INFORMATION, $"Call State changed to Disconnected");
                EventDispatcher.Instance.Unsubscribe("CallDisconnected", callConnectionId);
                reportCancellationTokenSource.Cancel();
                callTerminatedTask.SetResult(true);
            });

            //Subscribe to the call connected event
            var eventId = EventDispatcher.Instance.Subscribe("CallConnected", callConnectionId, callConnectedNotificaiton);

            //Subscribe to the call disconnected event
            var eventIdDisconnected = EventDispatcher.Instance.Subscribe("CallDisconnected", callConnectionId, callDisconnectedNotificaiton);
        }

        private void RegisterToPlayAudioResultEvent(string operationContext)
        {
            playAudioCompletedTask = new TaskCompletionSource<bool>(TaskContinuationOptions.RunContinuationsAsynchronously);
            reportCancellationToken.Register(() => playAudioCompletedTask.TrySetCanceled());

            var playCompletedNotification = new NotificationCallback((callEvent) =>
            {
                Task.Run(() =>
                {
                    Logger.LogMessage(Logger.MessageType.INFORMATION, $"Play audio status: Completed");
                    playAudioCompletedTask.TrySetResult(true);
                    EventDispatcher.Instance.Unsubscribe("PlayCompleted", operationContext);
                });
            });

            var playFailedNotification = new NotificationCallback((callEvent) =>
            {
                Task.Run(() =>
                {
                    Logger.LogMessage(Logger.MessageType.INFORMATION, $"Play audio status: Failed");
                    playAudioCompletedTask.TrySetResult(false);
                    EventDispatcher.Instance.Unsubscribe("PlayFailed", operationContext);
                });
            });

            var playCancelledNotification = new NotificationCallback((callEvent) =>
            {
                Task.Run(() =>
                {
                    Logger.LogMessage(Logger.MessageType.INFORMATION, $"Play audio status: Cancelled");
                    playAudioCompletedTask.TrySetResult(false);
                    EventDispatcher.Instance.Unsubscribe("PlayCancelled", operationContext);
                });
            });

            //Subscribe to event
            EventDispatcher.Instance.Subscribe("PlayCompleted", operationContext, playCompletedNotification);
            EventDispatcher.Instance.Subscribe("PlayFailed", operationContext, playFailedNotification);
            EventDispatcher.Instance.Subscribe("PlayCancelled", operationContext, playCancelledNotification);
        }

        private void RegisterToDtmfResultEvent(string callConnectionId)
        {
            toneReceivedCompleteTask = new TaskCompletionSource<bool>(TaskContinuationOptions.RunContinuationsAsynchronously);

            var dtmfReceivedEvent = new NotificationCallback((callEvent) =>
            {
                Task.Run(() =>
                {
                    var toneReceivedEvent = (RecognizeCompleted)callEvent;

                    //if (toneReceivedEvent.CollectTonesResult.Tones.Count != 0)
                    if (toneReceivedEvent.CollectTonesResult.Tones.Count != 0 && toneReceivedEvent.CollectTonesResult.Tones[0] != DtmfTone.Two)
                    {
                        Logger.LogMessage(Logger.MessageType.INFORMATION, $"Tone received --------- : {toneReceivedEvent.CollectTonesResult.Tones[0]}");
                        toneReceivedCompleteTask.TrySetResult(true);
                    }
                    else
                    {
                        toneReceivedCompleteTask.TrySetResult(false);
                    }
                    EventDispatcher.Instance.Unsubscribe("RecognizeCompleted", callConnectionId);

                    playAudioCompletedTask.TrySetResult(true);
                });
            });

            var dtmfFailedEvent = new NotificationCallback((callEvent) =>
            {
                Task.Run(() =>
                {
                    Logger.LogMessage(Logger.MessageType.INFORMATION, $"Failed to recognize any Dtmf tone");
                    toneReceivedCompleteTask.TrySetResult(false);
                    EventDispatcher.Instance.Unsubscribe("Recognizefailed", callConnectionId);
                });
            });

            //Subscribe to event
            EventDispatcher.Instance.Subscribe("RecognizeCompleted", callConnectionId, dtmfReceivedEvent);
            EventDispatcher.Instance.Subscribe("Recognizefailed", callConnectionId, dtmfFailedEvent);
        }

        private async Task<bool> AddParticipant(string addedParticipant)
        {
            addParticipantCompleteTask = new TaskCompletionSource<bool>(TaskContinuationOptions.RunContinuationsAsynchronously);

            var identifierKind = GetIdentifierKind(addedParticipant);
            if (identifierKind == CommunicationIdentifierKind.UnknownIdentity)
            {
                Logger.LogMessage(Logger.MessageType.INFORMATION, "Unknown identity provided. Enter valid phone number or communication user id");
                addParticipantCompleteTask.TrySetResult(true);
            }
            else
            {

                CommunicationIdentifier participant = null;
                RegisterToAddParticipantsResultEvent(callConnection.CallConnectionId);
                if (identifierKind == CommunicationIdentifierKind.UserIdentity)
                {
                    participant = new CommunicationUserIdentifier(addedParticipant);

                }
                else if (identifierKind == CommunicationIdentifierKind.PhoneIdentity)
                {
                    participant = new PhoneNumberIdentifier(addedParticipant);
                }

                List<CommunicationIdentifier> targets = new List<CommunicationIdentifier>();
                targets.Add(participant);

                AddParticipantsOptions addParticipantsOptions = new AddParticipantsOptions(targets);
                addParticipantsOptions.SourceCallerId = new PhoneNumberIdentifier(callConfiguration.SourcePhoneNumber);
                addParticipantsOptions.OperationContext = Guid.NewGuid().ToString();
                addParticipantsOptions.InvitationTimeoutInSeconds = 30;
                addParticipantsOptions.RepeatabilityHeaders = null;

                var response = this.callConnection.AddParticipants(addParticipantsOptions, CancellationToken.None);
                Logger.LogMessage(Logger.MessageType.INFORMATION, $"AddParticipantAsync response --> {response}");
                if (targets.Contains(participant))
                {
                    targets.Clear();
                }

            }

            Boolean addParticipantCompleted = false;
            try
            {
                addParticipantCompleted = await addParticipantCompleteTask.Task.ConfigureAwait(false);
            }
            catch (ThreadInterruptedException ex)
            {
                Logger.LogMessage(Logger.MessageType.ERROR, $"Failed to add participant InterruptedException -- > {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.LogMessage(Logger.MessageType.ERROR, $"Failed to add participant ExecutionException -- >{ex.Message} " );
            }
            
            return addParticipantCompleted;
        }

        private void RegisterToAddParticipantsResultEvent(string CallConnectionId)
        {
            addParticipantCompleteTask = new TaskCompletionSource<bool>(TaskContinuationOptions.RunContinuationsAsynchronously);

            var addParticipantsSucceededEvent = new NotificationCallback(async (callEvent) =>
            {
               
                    Logger.LogMessage(Logger.MessageType.INFORMATION, $"Add participant status completed--> {CallConnectionId}");
                    //EventDispatcher.Instance.Unsubscribe(addParticipantCompleteTask.ToString(), CallConnectionId);
                    EventDispatcher.Instance.Unsubscribe("AddParticipantsSucceeded", CallConnectionId);
                    Logger.LogMessage(Logger.MessageType.INFORMATION, "Sleeping for 60 seconds before proceeding further");
                    await Task.Delay(60 * 1000);

                    addParticipantCompleteTask.TrySetResult(true);
                

            });
            var addParticipantsFailedEvent = new NotificationCallback(async (callEvent) =>
            {
                Logger.LogMessage(Logger.MessageType.INFORMATION, $"Add participant status not completed");
                EventDispatcher.Instance.Unsubscribe("AddParticipantsFailed", CallConnectionId);
                addParticipantCompleteTask.TrySetResult(false);

            });
           
            //Subscribe to event
            EventDispatcher.Instance.Subscribe("AddParticipantsSucceeded", CallConnectionId, addParticipantsSucceededEvent);
            EventDispatcher.Instance.Subscribe("AddParticipantsFailed", CallConnectionId, addParticipantsFailedEvent);
            // EventDispatcher.Instance.Subscribe("ParticipantsUpdated", CallConnectionId, addParticipantsUpdatedEvent);
        }

        private CommunicationIdentifierKind GetIdentifierKind(string participantnumber)
        {
            //checks the identity type returns as string
            return Regex.Match(participantnumber, Constants.userIdentityRegex, RegexOptions.IgnoreCase).Success ? CommunicationIdentifierKind.UserIdentity :
                   Regex.Match(participantnumber, Constants.phoneIdentityRegex, RegexOptions.IgnoreCase).Success ? CommunicationIdentifierKind.PhoneIdentity :
                   CommunicationIdentifierKind.UnknownIdentity;
        }
    }
}
