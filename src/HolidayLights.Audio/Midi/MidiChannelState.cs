namespace HolidayLights.Audio.Midi;

/// <summary>
/// What one MIDI channel was told so far (program, controllers, pitch bend, channel pressure), so that "Resume" can send
/// it again (PRODUCT-SPEC 3.4.2: "program and controller state is re-sent"), and the song's own CC7 for volume scaling.
/// </summary>
internal sealed class MidiChannelState
{
    /// <summary>The GM default of CC7 (Volume).</summary>
    public const byte DefaultVolume = 100;

    private const int Unset = -1;
    private const byte BankSelectMsb = 0;
    private const byte BankSelectLsb = 32;
    private const byte VolumeController = 7;
    private const byte FirstModeMessage = 120;
    private const byte ResetAllControllers = 121;

    /// <summary>Controllers that are part of a parameter-number sequence; their meaning depends on the order sent.</summary>
    private static readonly byte[] ParameterControllers = [6, 38, 96, 97, 98, 99, 100, 101];

    /// <summary>The controllers "Reset All Controllers" clears (MIDI RP-015): modulation, expression, pedals, parameter numbers.</summary>
    private static readonly byte[] ResetByCc121 = [1, 11, 64, 65, 66, 67, 98, 99, 100, 101];

    private readonly int[] controllers = new int[FirstModeMessage];
    private int program = Unset;
    private int pitchBend = Unset;
    private int channelPressure = Unset;

    /// <summary>Creates the state of a freshly reset channel.</summary>
    public MidiChannelState() => Reset();

    /// <summary>The volume (CC7) the song set; the sequencer scales it by the Music Box volume.</summary>
    public byte SongVolume { get; private set; } = DefaultVolume;

    /// <summary>Forgets everything (GM System On or GS Reset).</summary>
    public void Reset()
    {
        Array.Fill(controllers, Unset);
        program = pitchBend = channelPressure = Unset;
        SongVolume = DefaultVolume;
    }

    /// <summary>Records a channel message that was sent.</summary>
    /// <param name="status">The status byte.</param>
    /// <param name="data1">The first data byte.</param>
    /// <param name="data2">The second data byte.</param>
    public void Observe(byte status, byte data1, byte data2)
    {
        switch (status & 0xF0)
        {
            case 0xB0 when data1 == ResetAllControllers:
                foreach (byte controller in ResetByCc121)
                {
                    controllers[controller] = Unset;
                }

                pitchBend = channelPressure = Unset;
                break;
            case 0xB0 when data1 < FirstModeMessage:
                controllers[data1] = data2;
                if (data1 == VolumeController)
                {
                    SongVolume = data2;
                }

                break;
            case 0xC0:
                program = data1;
                break;
            case 0xD0:
                channelPressure = data1;
                break;
            case 0xE0:
                pitchBend = data1 | (data2 << 7);
                break;
        }
    }

    /// <summary>
    /// Sends the state again: bank select and program first, then the other controllers in ascending order (CC7 scaled),
    /// pitch bend and channel pressure. Parameter-number sequences (RPN/NRPN) are left to the device, which keeps them.
    /// </summary>
    /// <param name="channel">The channel 0-15.</param>
    /// <param name="output">The output.</param>
    /// <param name="scaledVolume">CC7 to send (the song's volume scaled by the Music Box volume).</param>
    public void Replay(int channel, IMidiOutput output, byte scaledVolume)
    {
        byte control = (byte)(0xB0 | channel);
        SendIfSet(output, control, BankSelectMsb);
        SendIfSet(output, control, BankSelectLsb);
        if (program != Unset)
        {
            output.SendShort((byte)(0xC0 | channel), (byte)program, 0);
        }

        for (byte controller = 1; controller < FirstModeMessage; controller++)
        {
            if (controller != BankSelectLsb && controller != VolumeController && Array.IndexOf(ParameterControllers, controller) < 0)
            {
                SendIfSet(output, control, controller);
            }
        }

        output.SendShort(control, VolumeController, scaledVolume);
        if (pitchBend != Unset)
        {
            output.SendShort((byte)(0xE0 | channel), (byte)(pitchBend & 0x7F), (byte)(pitchBend >> 7));
        }

        if (channelPressure != Unset)
        {
            output.SendShort((byte)(0xD0 | channel), (byte)channelPressure, 0);
        }
    }

    private void SendIfSet(IMidiOutput output, byte control, byte controller)
    {
        if (controllers[controller] != Unset)
        {
            output.SendShort(control, controller, (byte)controllers[controller]);
        }
    }
}
