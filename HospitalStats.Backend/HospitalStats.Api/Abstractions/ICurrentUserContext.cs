namespace HospitalStats.Api.Abstractions;

/// <summary>
/// Abstraction over HTTP/JWT context for resolving user identity claims.
/// Decouples QueryExecutionService from IHttpContextAccessor so the core
/// engine can operate without ASP.NET dependency.
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>
    /// Returns context values derived from the current user's identity.
    /// Keys are e.g. "UserId", "DeptName" — matched to QueryFilter.ContextKey.
    /// </summary>
    Dictionary<string, string> GetContextValues();
}
