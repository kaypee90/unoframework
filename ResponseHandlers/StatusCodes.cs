namespace unoframework.ResponseHandlers;

public enum StatusCodes
{
    Ok = 200,
    NoContent = 204,
    BadRequest = 400,
    Forbidden = 403,
    NotFound = 404,
    MethodNotAllowed = 405,
    InternalServerError = 500,
    ServiceUnavailable = 503,
}
