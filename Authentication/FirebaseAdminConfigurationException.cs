namespace LadingSystem.Authentication;

public sealed class FirebaseAdminConfigurationException(Exception innerException)
    : Exception("Firebase Admin credentials could not be loaded. Set GOOGLE_APPLICATION_CREDENTIALS "
        + "to a readable service-account JSON file for searoute-d3c1b, or configure Application Default "
        + "Credentials, then restart the application. Firebase CLI login is not sufficient.", innerException);
