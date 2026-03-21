namespace FamilyHQ.Simulator.DTOs;

public record SimulatorAttendee(string Email);
public record SimulatorPatchAttendeesRequest(IReadOnlyList<SimulatorAttendee> Attendees);
