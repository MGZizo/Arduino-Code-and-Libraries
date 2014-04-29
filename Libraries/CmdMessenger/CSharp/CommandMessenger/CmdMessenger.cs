﻿#region CmdMessenger - MIT - (c) 2014 Thijs Elenbaas.
/*
  CmdMessenger - library that provides command based messaging

  Permission is hereby granted, free of charge, to any person obtaining
  a copy of this software and associated documentation files (the
  "Software"), to deal in the Software without restriction, including
  without limitation the rights to use, copy, modify, merge, publish,
  distribute, sublicense, and/or sell copies of the Software, and to
  permit persons to whom the Software is furnished to do so, subject to
  the following conditions:

  The above copyright notice and this permission notice shall be
  included in all copies or substantial portions of the Software.

  Copyright 2014 - Thijs Elenbaas
*/
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using CommandMessenger.TransportLayer;

namespace CommandMessenger
{
    public enum ClearQueue
    {
        KeepQueue,
        ClearSendQueue,
        ClearReceivedQueue,
        ClearSendAndReceivedQueue,
    }

    public enum BoardType
    {
        Bit16,
        Bit32,
    }

    /// <summary> Command messenger main class  </summary>
    public class CmdMessenger : DisposableObject
    {
        
        //public EventHandler NewLinesReceived;                               // Event handler for new lines received
        //public EventHandler NewLineReceived;	                            // Event handler for a new line received
        public event NewLineEvent.NewLineHandler NewLineReceived;
        public event NewLineEvent.NewLineHandler NewLineSent;

        //public EventHandler NewLineSent;	                                // The new line sent
        
       
        private CommunicationManager _communicationManager;                 // The communication manager
        private Sender _sender;                                             // The command sender

        private char _fieldSeparator;                                       // The field separator
        private char _commandSeparator;                                     // The command separator
        private bool _printLfCr;                                            // Add Linefeed + CarriageReturn 
        private BoardType _boardType;
        private MessengerCallbackFunction _defaultCallback;                 // The default callback
        private Dictionary<int, MessengerCallbackFunction> _callbackList;   // List of callbacks

        private SendCommandQueue _sendCommandQueue;                         // The queue of commands to be sent
        private ReceiveCommandQueue _receiveCommandQueue;                   // The queue of commands to be processed

        private Logger _sendCommandLogger = new Logger(@"d:\sendCommands.txt");
        /// <summary> Definition of the messenger callback function. </summary>
        /// <param name="receivedCommand"> The received command. </param>
        public delegate void MessengerCallbackFunction(ReceivedCommand receivedCommand);

        /// <summary> Embedded Processor type. Needed to translate variables between sides. </summary>
        /// <value> The current received line. </value>
        public BoardType BoardType {
            get { return _boardType;  }
            set
            {
                _boardType = value;
                Command.BoardType = _boardType;
            }
        }


        /// <summary> Gets or sets a whether to print a line feed carriage return after each command. </summary>
        /// <value> true if print line feed carriage return, false if not. </value>
        public bool PrintLfCr { 
            get { return _printLfCr; } 
            set {
                _printLfCr = value;
                Command.PrintLfCr = _printLfCr;
                _sender.PrintLfCr = _printLfCr;
            } 
        }

        /// <summary> Gets or sets the current received command line. </summary>
        /// <value> The current received line. </value>
        public String CurrentReceivedLine { get; private set; }



        /// <summary> Gets or sets the currently sent line. </summary>
        /// <value> The currently sent line. </value>
        public String CurrentSentLine { get; private set; }

        // Enable logging send commands to file
        public bool LogSendCommandsEnabled
        {
            get { return _sendCommandLogger.isEnabled; }
            set { 
                _sendCommandLogger.isEnabled = value;
                if  (!_sendCommandLogger.isOpen) {
                    _sendCommandLogger.Open();
                }
            }
        }

        /// <summary> Gets or sets the log file of send commands. </summary>
        /// <value> The logfile name for send commands. </value>
        public String LogFileSendCommands
        {
            get { return _sendCommandLogger.LogFileName; }
            set { _sendCommandLogger.LogFileName = value; }
        }

   

        /// <summary> Gets or sets the log file of receive commands. </summary>
        /// <value> The logfile name for receive commands. </value>
        public String LogFileReceiveCommands { get; set; }

        // The control to invoke the callback on
        private Control _controlToInvokeOn;
        


        /// <summary> Constructor. </summary>
        /// <param name="transport"> The transport layer. </param>
        public CmdMessenger(ITransport transport)
        {
            Init(transport, ',', ';', '/');
        }

        /// <summary> Constructor. </summary>
        /// <param name="transport"> The transport layer. </param>
        /// <param name="fieldSeparator"> The field separator. </param>
        public CmdMessenger(ITransport transport, char fieldSeparator)
        {
            Init(transport, fieldSeparator, ';', '/');
        }

        /// <summary> Constructor. </summary>
        /// <param name="transport">   The transport layer. </param>
        /// <param name="fieldSeparator">   The field separator. </param>
        /// <param name="commandSeparator"> The command separator. </param>
        public CmdMessenger(ITransport transport, char fieldSeparator, char commandSeparator)
        {
            Init(transport, fieldSeparator, commandSeparator, commandSeparator);
        }

        /// <summary> Constructor. </summary>
        /// <param name="transport">   The transport layer. </param>
        /// <param name="fieldSeparator">   The field separator. </param>
        /// <param name="commandSeparator"> The command separator. </param>
        /// <param name="escapeCharacter">  The escape character. </param>
        public CmdMessenger(ITransport transport, char fieldSeparator, char commandSeparator,
                            char escapeCharacter)
        {
            Init(transport, fieldSeparator, commandSeparator, escapeCharacter);
        }

        /// <summary> Initialises this object. </summary>
        /// <param name="transport">   The transport layer. </param>
        /// <param name="fieldSeparator">   The field separator. </param>
        /// <param name="commandSeparator"> The command separator. </param>
        /// <param name="escapeCharacter">  The escape character. </param>
        private void Init(ITransport transport, char fieldSeparator, char commandSeparator,
                          char escapeCharacter)
        {           
            _controlToInvokeOn = null;

            
            _receiveCommandQueue  = new ReceiveCommandQueue(DisposeStack, this);
            _communicationManager = new CommunicationManager(DisposeStack, transport, _receiveCommandQueue, commandSeparator, fieldSeparator, escapeCharacter);
            _sender               = new Sender(_communicationManager, _receiveCommandQueue);
            _sendCommandQueue     = new SendCommandQueue(DisposeStack, this, _sender);
           
            _receiveCommandQueue.NewLineReceived += new NewLineEvent.NewLineHandler((o, e) => InvokeNewLineEvent(NewLineReceived, e));
            _sendCommandQueue.NewLineSent        += new NewLineEvent.NewLineHandler((o, e) => InvokeNewLineEvent(NewLineSent, e));

            _fieldSeparator = fieldSeparator;
            _commandSeparator = commandSeparator;
            PrintLfCr = false;

            Command.FieldSeparator = _fieldSeparator;
            Command.CommandSeparator = _commandSeparator;
            Command.PrintLfCr = PrintLfCr;            

            Escaping.EscapeChars(_fieldSeparator, _commandSeparator, escapeCharacter);
            _callbackList = new Dictionary<int, MessengerCallbackFunction>();
            CurrentSentLine = "";
            CurrentReceivedLine = "";
        }

        //void ReceiveCommandQueueNewLineReceived(object sender, NewLineEvent.NewLineArgs e)
        //{
        //    InvokeNewLineEvent(NewLineReceived, e);
        //}

        public void SetSingleCore()
        {
            var proc = Process.GetCurrentProcess();
            foreach (ProcessThread pt in proc.Threads)
            {
                if (pt.ThreadState != ThreadState.Terminated)
                {
                    try
                    {
                        pt.IdealProcessor = 0;
                        pt.ProcessorAffinity = (IntPtr) 1;
                    }
                    catch (Exception)
                    {
                    }

                }
            }
        }
        /// <summary> Sets a control to invoke on. </summary>
        /// <param name="controlToInvokeOn"> The control to invoke on. </param>
        public void SetControlToInvokeOn(Control controlToInvokeOn)
        {
            _controlToInvokeOn = controlToInvokeOn;
        }

        /// <summary>  Stop listening and end serial port connection. </summary>
        /// <returns> true if it succeeds, false if it fails. </returns>
        public bool StopListening()
        {
            return _communicationManager.StopListening();
        }

        /// <summary> Starts serial port connection and start listening. </summary>
        /// <returns> true if it succeeds, false if it fails. </returns>
        public bool StartListening()
        {
            if (_communicationManager.StartListening())
            {
                // Timestamp of this command is same as time stamp of serial line
                LastLineTimeStamp = _communicationManager.LastLineTimeStamp;
                return true;
            }
            return false;
        }

        /// <summary> Attaches default callback for unsupported commands. </summary>
        /// <param name="newFunction"> The callback function. </param>
        public void Attach(MessengerCallbackFunction newFunction)
        {
            _defaultCallback = newFunction;
        }

        /// <summary> Attaches default callback for certain Message ID. </summary>
        /// <param name="messageId">   Command ID. </param>
        /// <param name="newFunction"> The callback function. </param>
        public void Attach(int messageId, MessengerCallbackFunction newFunction)
        {
            _callbackList[messageId] = newFunction;
        }

        /// <summary> Gets or sets the time stamp of the last command line received. </summary>
        /// <value> The last line time stamp. </value>
        public long LastLineTimeStamp { get; private set; }


        /// <summary> Handle message. </summary>
        /// <param name="receivedCommand"> The received command. </param>
        public void HandleMessage(ReceivedCommand receivedCommand)
        {
            CurrentReceivedLine = receivedCommand.RawString;
            // Send message that a new line has been received and is due to be processed
            //InvokeEvent(NewLineReceived);

            MessengerCallbackFunction callback = null;
            if (receivedCommand.Ok)
            {
                if (_callbackList.ContainsKey(receivedCommand.CmdId))
                {
                    callback = _callbackList[receivedCommand.CmdId];
                }
                else
                {
                    if (_defaultCallback != null) callback = _defaultCallback;
                }
            }
            else
            {
                // Empty command
                receivedCommand = new ReceivedCommand();
            }
            InvokeCallBack(callback, receivedCommand);
        }

        /// <summary> Sends a command. 
        /// 		  If no command acknowledge is requested, the command will be send asynchronously: it will be put on the top of the send queue
        ///  		  If a  command acknowledge is requested, the command will be send synchronously:  the program will block until the acknowledge command 
        ///  		  has been received or the timeout has expired. </summary>
        /// <param name="sendCommand"> The command to sent. </param>
        public ReceivedCommand SendCommand(SendCommand sendCommand)
        {
            return SendCommand(sendCommand, ClearQueue.KeepQueue);
        }

                /// <summary> Sends a command. 
        /// 		  If no command acknowledge is requested, the command will be send asynchronously: it will be put on the top of the send queue
        ///  		  If a  command acknowledge is requested, the command will be send synchronously:  the program will block until the acknowledge command 
        ///  		  has been received or the timeout has expired.
        ///  		  Based on ClearQueueState, the send- and receive-queues are left intact or are cleared</summary>
        /// <param name="sendCommand"> The command to sent. </param>
        /// <param name="clearQueueState"> Property to optionally clear the send and receive queues</param>
        /// <returns> A received command. The received command will only be valid if the ReqAc of the command is true. </returns>
        public ReceivedCommand SendCommand(SendCommand sendCommand, ClearQueue clearQueueState)
        {
            return SendCommand(sendCommand, clearQueueState, true);
        }

        /// <summary> Sends a command. 
        /// 		  If no command acknowledge is requested, the command will be send asynchronously: it will be put on the top of the send queue
        ///  		  If a  command acknowledge is requested, the command will be send synchronously:  the program will block until the acknowledge command 
        ///  		  has been received or the timeout has expired.
        ///  		  Based on ClearQueueState, the send- and receive-queues are left intact or are cleared</summary>
        /// <param name="sendCommand"> The command to sent. </param>
        /// <param name="clearQueueState"> Property to optionally clear the send and receive queues</param>
        /// <param name="sendQueued"> Property to indicate if command needs to be send queued or directly</param>
        /// <returns> A received command. The received command will only be valid if the ReqAc of the command is true. </returns>
        public ReceivedCommand SendCommand(SendCommand sendCommand, ClearQueue clearQueueState, bool sendQueued)
        {
            _sendCommandLogger.LogLine(sendCommand.CommandString());

            if (clearQueueState == ClearQueue.ClearReceivedQueue || 
                clearQueueState == ClearQueue.ClearSendAndReceivedQueue)
            {
                // Clear receive queue
                _receiveCommandQueue.Clear(); 
            }

            if (clearQueueState == ClearQueue.ClearSendQueue || 
                clearQueueState == ClearQueue.ClearSendAndReceivedQueue)
            {
                // Clear send queue
                _sendCommandQueue.Clear();
            }

            if (sendCommand.ReqAc || !sendQueued)
            {
                // Directly call execute command
                //if (NewLineSent != null) NewLineSent(this, new NewLineEvent.NewLineArgs(sendCommand));
                InvokeNewLineEvent(NewLineSent, new NewLineEvent.NewLineArgs(sendCommand));
                return _sender.ExecuteSendCommand(sendCommand, clearQueueState);
            }
            
            // Put command at top of command queue
            _sendCommandQueue.SendCommand(sendCommand);
            return new ReceivedCommand();
        }





        /// <summary> Put the command at the back of the sent queue.</summary>
        /// <param name="sendCommand"> The command to sent. </param>
        public void QueueCommand(SendCommand sendCommand)
        {
            _sendCommandQueue.QueueCommand(sendCommand);
        }

        /// <summary> Put  a command wrapped in a strategy at the back of the sent queue.</summary>
        /// <param name="commandStrategy"> The command strategy. </param>
        public void QueueCommand(CommandStrategy commandStrategy)
        {
            _sendCommandQueue.QueueCommand(commandStrategy);
        }

        /// <summary> Adds a general command strategy to the receive queue. This will be executed on every enqueued and dequeued command.  </summary>
        /// <param name="generalStrategy"> The general strategy for the receive queue. </param>
        public void AddReceiveCommandStrategy(GeneralStrategy generalStrategy) 
        {
            _receiveCommandQueue.AddGeneralStrategy(generalStrategy);
        }

        /// <summary> Adds a general command strategy to the send queue. This will be executed on every enqueued and dequeued command.  </summary>
        /// <param name="generalStrategy"> The general strategy for the send queue. </param>
        public void AddSendCommandStrategy(GeneralStrategy generalStrategy)
        {
            _sendCommandQueue.AddGeneralStrategy(generalStrategy);
        }

        /// <summary> Clears the receive queue. </summary>
        public void ClearReceiveQueue()
        {
            _receiveCommandQueue.Clear();
        }

        /// <summary> Clears the send queue. </summary>
        public void ClearSendQueue()
        {
            _sendCommandQueue.Clear();
        }

        /// <summary> Helper function to Invoke or directly call event. </summary>
        /// <param name="newLineHandler"> The event handler. </param>
        /// <param name="newLineArgs"></param>
        private void InvokeNewLineEvent(NewLineEvent.NewLineHandler newLineHandler, NewLineEvent.NewLineArgs newLineArgs)
        {
            try
            {
                if (newLineHandler != null)
                {
                    if (_controlToInvokeOn != null && _controlToInvokeOn.InvokeRequired)
                    {
                        //Asynchronously call on UI thread
                        _controlToInvokeOn.Invoke((MethodInvoker)(() => newLineHandler(this, newLineArgs)));
                    }
                    else
                    {
                        //Directly call
                        newLineHandler(this, newLineArgs);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary> Helper function to Invoke or directly call callback function. </summary>
        /// <param name="messengerCallbackFunction"> The messenger callback function. </param>
        /// <param name="command">                   The command. </param>
        private void InvokeCallBack(MessengerCallbackFunction messengerCallbackFunction, ReceivedCommand command)
        {
            if (messengerCallbackFunction != null)
            {
                if (_controlToInvokeOn != null && _controlToInvokeOn.InvokeRequired)
                {
                    //Asynchronously call on UI thread
                    _controlToInvokeOn.Invoke(new MessengerCallbackFunction(messengerCallbackFunction), (object) command);
                }
                else
                {
                    //Directly call
                    messengerCallbackFunction(command);
                }
            }
        }



        /// <summary> Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources. </summary>
        /// <param name="disposing"> true if resources should be disposed, false if not. </param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _controlToInvokeOn = null;
                _receiveCommandQueue.ThreadRunState = CommandQueue.ThreadRunStates.Stop;
                _sendCommandLogger.Close();
            }
            base.Dispose(disposing);
        }
    }
}