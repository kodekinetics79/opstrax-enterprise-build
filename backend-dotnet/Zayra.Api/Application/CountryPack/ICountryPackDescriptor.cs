namespace Zayra.Api.Application.CountryPack;

// Returns static metadata about a country pack — scheme names, formula descriptions,
// WPS format labels.  No DB access; implementations are registered as singletons.
public interface ICountryPackDescriptor
{
    PackDescriptor GetDescriptor();
}
